[CmdletBinding()]
param(
  [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
  [string]$Runtime = 'win-x64',
  [string]$OutputDirectory = '',
  [switch]$SelfContained,
  [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repo 'artifacts\publish' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
$artifacts = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
if (-not $out.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "OutputDirectory must stay under artifacts." }
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
Push-Location $repo
try {
  if (-not $SkipTests) {
    dotnet build TruyTimDanChu.csproj -c $Configuration
    dotnet build Server/TruyTimDanChu.Server.csproj -c $Configuration
    dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c $Configuration --no-restore
    dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c $Configuration --no-restore
    dotnet run --project Tests/GameSmoke.csproj -c $Configuration --no-build
  }
  $clientOut = Join-Path $out 'client'; $serverOut = Join-Path $out 'server'
  $rid = if ([string]::IsNullOrWhiteSpace($Runtime)) { @() } else { @('--runtime', $Runtime) }
  $sc = if ($SelfContained) { @('--self-contained', 'true') } else { @('--self-contained', 'false') }
  # Blazor WebAssembly publish owns its runtime model; passing --self-contained false breaks trimming on net10.
  dotnet publish TruyTimDanChu.csproj -c $Configuration -o $clientOut @rid
  dotnet publish Server/TruyTimDanChu.Server.csproj -c $Configuration -o $serverOut @rid @sc
  $clientWww = Join-Path $clientOut 'wwwroot'; $serverWww = Join-Path $serverOut 'wwwroot'
  if (-not (Test-Path -LiteralPath $clientWww)) { throw 'Client publish wwwroot missing.' }
  New-Item -ItemType Directory -Path $serverWww -Force | Out-Null
  Get-ChildItem -LiteralPath $clientWww -Force | Copy-Item -Destination $serverWww -Recurse -Force
  Copy-Item -LiteralPath (Join-Path $repo 'Server/appsettings.Production.example.json') -Destination (Join-Path $serverOut 'appsettings.Production.example.json') -Force
  Get-ChildItem -LiteralPath $out -Filter 'appsettings.Development.json' -File -Recurse | Remove-Item -Force
  $commit = (& git rev-parse HEAD 2>$null); if ($LASTEXITCODE -ne 0) { $commit = 'uncommitted' }
  $manifest = [ordered]@{ builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); configuration = $Configuration; runtime = $Runtime; commit = $commit; protocolVersion = 1; contentVersion = 'minh-dang-v1'; files = @{} }
  Get-ChildItem -LiteralPath $serverOut -File -Recurse | ForEach-Object { $relative = $_.FullName.Substring($serverOut.Length + 1); $manifest.files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
  $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding utf8
  $scan = Get-ChildItem -LiteralPath $out -File -Recurse | Where-Object { $_.Extension -in '.json','.js','.html','.config' } | Get-Content -Raw
  if ($scan -match 'localhost|appsettings\.Development|raw-admin-token|certificatePassword') { throw 'Publish secret/dev-host scan failed.' }
  Write-Host "Publish ready: $out"
}
finally { Pop-Location }

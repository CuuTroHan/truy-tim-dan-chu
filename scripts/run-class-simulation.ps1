[CmdletBinding()]
param(
  [ValidateSet('Full','Dry')][string]$Mode = 'Full',
  [switch]$Headless,
  [switch]$SkipPublish,
  [string]$FfmpegPath = '',
  [switch]$KeepServer
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss')
$runDir = Join-Path $repo ("artifacts\simulation\" + $runId)
$publishDir = Join-Path $runDir 'publish'
$dbPath = Join-Path $runDir 'simulation.db'
$serverLog = Join-Path $runDir 'server.log'
$serverError = Join-Path $runDir 'server-error.log'
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

function Get-FreeLocalPort {
  $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
  $listener.Start()
  try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
  finally { $listener.Stop() }
}

function Wait-Endpoint([string]$Url) {
  for ($i = 0; $i -lt 45; $i++) {
    try {
      $health = Invoke-WebRequest -Uri ($Url + '/health') -UseBasicParsing -TimeoutSec 2
      $ready = Invoke-WebRequest -Uri ($Url + '/ready') -UseBasicParsing -TimeoutSec 2
      if ($health.StatusCode -eq 200 -and $ready.StatusCode -eq 200) { return }
    } catch { Start-Sleep -Seconds 1; continue }
    Start-Sleep -Seconds 1
  }
  throw "Published simulation server did not pass /health and /ready: $Url"
}

function Resolve-PortableFfmpeg {
  param([string]$RequestedPath)
  if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
    $resolved = (Resolve-Path -LiteralPath $RequestedPath).Path
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "FfmpegPath is not a file: $resolved" }
    return $resolved
  }
  $command = Get-Command ffmpeg -ErrorAction SilentlyContinue
  if ($null -ne $command) { return $command.Source }

  $toolsDir = Join-Path $repo 'artifacts\tools'
  $zip = Join-Path $toolsDir 'ffmpeg-release-essentials.zip'
  $checksumFile = Join-Path $toolsDir 'ffmpeg-release-essentials.zip.sha256'
  $extract = Join-Path $toolsDir 'ffmpeg-release-essentials'
  $ffmpeg = Get-ChildItem -LiteralPath $extract -Filter ffmpeg.exe -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($null -ne $ffmpeg) { return $ffmpeg.FullName }
  New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
  $zipUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
  $checksumUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256'
  Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing
  Invoke-WebRequest -Uri $checksumUrl -OutFile $checksumFile -UseBasicParsing
  $expected = ((Get-Content -LiteralPath $checksumFile -Raw).Trim() -split '\s+')[0].ToUpperInvariant()
  $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToUpperInvariant()
  if ($expected -notmatch '^[A-F0-9]{64}$' -or $actual -ne $expected) { throw 'Portable FFmpeg checksum verification failed; raw WebM files were preserved.' }
  Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force
  $ffmpeg = Get-ChildItem -LiteralPath $extract -Filter ffmpeg.exe -File -Recurse | Select-Object -First 1
  if ($null -eq $ffmpeg) { throw 'Portable FFmpeg archive did not contain ffmpeg.exe.' }
  return $ffmpeg.FullName
}

$server = $null
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
$oldConnection = $env:ConnectionStrings__GameDatabase
$oldRooms = $env:Multiplayer__Rooms__MaxPlayersPerRoom
$oldTeams = $env:Multiplayer__Rooms__MaxTeamsPerRoom
$oldTeamCapacity = $env:Multiplayer__Rooms__MaxPlayersPerTeam
$oldMaxTime = $env:Multiplayer__Rooms__MaxTimeLimitSeconds
$oldRequireHttps = $env:Multiplayer__Rooms__RequireHttps
try {
  Push-Location $repo
  if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows.ps1') -Configuration Release -OutputDirectory $publishDir -SkipTests
  }
  $serverDll = Join-Path $publishDir 'server\TruyTimDanChu.Server.dll'
  if (-not (Test-Path -LiteralPath $serverDll)) { throw "Published server missing: $serverDll" }
  dotnet build Tests/Multiplayer.Simulation/Multiplayer.Simulation.csproj -c Release
  if ($LASTEXITCODE -ne 0) { throw 'Simulation runner build failed.' }
  $port = Get-FreeLocalPort
  $baseUrl = "http://127.0.0.1:$port"
  # These values are process-local and do not touch appsettings.json or any existing database.
  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ConnectionStrings__GameDatabase = "Data Source=$dbPath"
  $env:Multiplayer__Rooms__MaxPlayersPerRoom = '64'
  $env:Multiplayer__Rooms__MaxTeamsPerRoom = '8'
  $env:Multiplayer__Rooms__MaxPlayersPerTeam = '8'
  $env:Multiplayer__Rooms__MaxTimeLimitSeconds = '1800'
  $env:Multiplayer__Rooms__RequireHttps = 'false'
  $server = Start-Process -FilePath dotnet -ArgumentList @($serverDll, '--urls', $baseUrl) -WorkingDirectory (Split-Path $serverDll) -RedirectStandardOutput $serverLog -RedirectStandardError $serverError -WindowStyle Hidden -PassThru
  Wait-Endpoint $baseUrl
  $runnerArgs = @('run','--project','Tests/Multiplayer.Simulation/Multiplayer.Simulation.csproj','-c','Release','--no-build','--',"--url=$baseUrl","--output=$runDir",("--mode=" + $Mode.ToLowerInvariant()),("--headless=" + [bool]$Headless))
  & dotnet @runnerArgs
  $runnerExit = $LASTEXITCODE
  if ($runnerExit -ne 0) { throw "Simulation runner exited with $runnerExit. Raw artifacts are retained in $runDir" }
  if ($Mode -eq 'Full') {
    $ffmpeg = Resolve-PortableFfmpeg -RequestedPath $FfmpegPath
    $raw = Join-Path $runDir 'raw-webm'
    $admin = Join-Path $raw 'admin.webm'
    $team01a = Join-Path $raw 'team01-player1.webm'; $team01b = Join-Path $raw 'team01-player2.webm'
    $team08a = Join-Path $raw 'team08-player1.webm'; $team08b = Join-Path $raw 'team08-player2.webm'
    if (@(@($admin,$team01a,$team01b,$team08a,$team08b) | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -gt 0) { throw 'One or more required browser WebM recordings are missing.' }
    $mp4 = Join-Path $runDir 'session-60p-8groups.mp4'
    & $ffmpeg -y -i $admin -i $team01a -i $team01b -i $team08a -i $team08b -filter_complex '[0:v]scale=1920:360:force_original_aspect_ratio=decrease,pad=1920:360:(ow-iw)/2:(oh-ih)/2[a];[1:v]scale=960:360:force_original_aspect_ratio=decrease,pad=960:360:(ow-iw)/2:(oh-ih)/2[b];[2:v]scale=960:360:force_original_aspect_ratio=decrease,pad=960:360:(ow-iw)/2:(oh-ih)/2[c];[3:v]scale=960:360:force_original_aspect_ratio=decrease,pad=960:360:(ow-iw)/2:(oh-ih)/2[d];[4:v]scale=960:360:force_original_aspect_ratio=decrease,pad=960:360:(ow-iw)/2:(oh-ih)/2[e];[b][c]hstack[row1];[d][e]hstack[row2];[a][row1][row2]vstack=inputs=3,format=yuv420p[out]' -map '[out]' -r 30 -c:v libx264 -movflags +faststart $mp4
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $mp4)) { throw 'MP4 transcode failed; WebM files were retained and the run is FAIL.' }
    $probe = & $ffmpeg -i $mp4 2>&1 | Out-String
    if ($probe -notmatch 'Duration: 00:0[6-8]:') { throw 'MP4 is outside the mandatory 6–8 minute duration window.' }
    $version = (& $ffmpeg -version | Select-Object -First 1)
    @{ ffmpeg = $version; path = $ffmpeg; mp4 = 'session-60p-8groups.mp4'; layout = 'admin top; team01 players middle left/right; team08 players bottom left/right'; verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDir 'video.json') -Encoding utf8
    Add-Content -LiteralPath (Join-Path $runDir 'BIEN_BAN_MO_PHONG.md') -Value "`n## Video`n`n- MP4 H.264 1920×1080, 30 fps, yuv420p, fast-start: `session-60p-8groups.mp4`.`n- 5 WebM gốc: admin; 2 người chơi Nhóm 01; 2 người chơi Nhóm 08.`n- $version"
  }
  Write-Host "PASS: $runDir"
}
catch {
  $message = $_.Exception.Message
  $report = Join-Path $runDir 'BIEN_BAN_MO_PHONG.md'
  if (Test-Path -LiteralPath $report) {
    (Get-Content -LiteralPath $report -Raw).Replace('**PASS**', '**FAIL**') | Set-Content -LiteralPath $report -Encoding utf8
  }
  $sessionFile = Join-Path $runDir 'session.json'
  if (Test-Path -LiteralPath $sessionFile) {
    $session = Get-Content -LiteralPath $sessionFile -Raw | ConvertFrom-Json
    $session.pass = $false
    $session | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sessionFile -Encoding utf8
  }
  Add-Content -LiteralPath (Join-Path $runDir 'BIEN_BAN_MO_PHONG.md') -Value "`n## Script failure`n`n$message" -ErrorAction SilentlyContinue
  Write-Error $message
  exit 1
}
finally {
  if (-not $KeepServer -and $null -ne $server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
  $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
  $env:ConnectionStrings__GameDatabase = $oldConnection
  $env:Multiplayer__Rooms__MaxPlayersPerRoom = $oldRooms
  $env:Multiplayer__Rooms__MaxTeamsPerRoom = $oldTeams
  $env:Multiplayer__Rooms__MaxPlayersPerTeam = $oldTeamCapacity
  $env:Multiplayer__Rooms__MaxTimeLimitSeconds = $oldMaxTime
  $env:Multiplayer__Rooms__RequireHttps = $oldRequireHttps
  Pop-Location
}

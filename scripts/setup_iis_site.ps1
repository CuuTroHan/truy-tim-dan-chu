# =====================================================================
# SCRIPT KHOI TAO SITE TRUY TIM DAN CHU TREN IIS (CHAY 1 LAN DUY NHAT)
# Chay script nay bang PowerShell (Run as Administrator) tren Windows VPS
# =====================================================================

param (
    [int]$Port = 5080,
    [string]$SiteName = "TruyTimDanChu",
    [string]$PhysicalPath = "C:\inetpub\wwwroot\truy-tim-dan-chu"
)

Write-Host "=== BAT DAU CAU HINH IIS CHO TRUY TIM DAN CHU ===" -ForegroundColor Cyan

# 1. Kiem tra va bat module WebSockets tren IIS
Write-Host "1. Kiem tra tinh nang WebSocket Protocol..." -ForegroundColor Yellow
try {
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All -NoRestart -ErrorAction SilentlyContinue
    Write-Host "-> Da bat WebSocket Protocol thanh cong." -ForegroundColor Green
} catch {
    Write-Host "-> Chu y: Neu dang dung Windows Server, hay chay: Install-WindowsFeature Web-WebSockets" -ForegroundColor Gray
}

# 2. Tao thu muc C:\inetpub\truy-tim-dan-chu
Write-Host "2. Tao thu muc vat ly: $PhysicalPath..." -ForegroundColor Yellow
if (-not (Test-Path $PhysicalPath)) {
    New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null
}

# 3. Phan quyen cho IIS_IUSRS tren thu muc
Write-Host "3. Cap quyen truy cap cho IIS_IUSRS..." -ForegroundColor Yellow
$acl = Get-Acl $PhysicalPath
$permission = "IIS_IUSRS", "ReadAndExecute,ListDirectory,Write", "ContainerInherit,ObjectInherit", "None", "Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.AddAccessRule($accessRule)
Set-Acl $PhysicalPath $acl
Write-Host "-> Da phan quyen IIS_IUSRS thanh cong." -ForegroundColor Green

# 4. Import module quan tri IIS
Import-Module WebAdministration

# 5. Tao Application Pool "No Managed Code"
Write-Host "4. Tao Application Pool '$SiteName' (No Managed Code)..." -ForegroundColor Yellow
if (-not (Test-Path "IIS:\AppPools\$SiteName")) {
    $pool = New-Item "IIS:\AppPools\$SiteName"
    $pool.managedRuntimeVersion = "" # "" nghia la No Managed Code cho .NET Core/.NET 10
    $pool | Set-Item
    Write-Host "-> Da tao AppPool '$SiteName' thanh cong." -ForegroundColor Green
} else {
    Set-ItemProperty "IIS:\AppPools\$SiteName" -Name "managedRuntimeVersion" -Value ""
    Write-Host "-> AppPool '$SiteName' da ton tai, da dat ve 'No Managed Code'." -ForegroundColor Green
}

# 6. Tao Website tren IIS
Write-Host "5. Tao Website '$SiteName' tren Port $Port..." -ForegroundColor Yellow
if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Item "IIS:\Sites\$SiteName" -bindings @{protocol="http";bindingInformation="*:${Port}:"} -physicalPath $PhysicalPath | Out-Null
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name "applicationPool" -Value $SiteName
    Write-Host "-> Da tao Website '$SiteName' tren Port $Port thanh cong." -ForegroundColor Green
} else {
    Write-Host "-> Website '$SiteName' da ton tai tren IIS." -ForegroundColor Yellow
}

# 7. Mo port Firewall tren Windows
Write-Host "6. Mo cong $Port tren Windows Defender Firewall..." -ForegroundColor Yellow
$firewallRule = Get-NetFirewallRule -DisplayName "$SiteName Port $Port" -ErrorAction SilentlyContinue
if (-not $firewallRule) {
    New-NetFirewallRule -DisplayName "$SiteName Port $Port" -Direction Inbound -LocalPort $Port -Protocol TCP -Action Allow | Out-Null
    Write-Host "-> Da tao Rule mo Firewall cho port $Port thanh cong." -ForegroundColor Green
} else {
    Write-Host "-> Rule Firewall cho port $Port da ton tai." -ForegroundColor Green
}

Write-Host "`n=== HOAN TAT CAU HINH IIS! ===" -ForegroundColor Cyan
Write-Host "Dia chi truy cap game qua IP VPS: http://<IP_VPS>:$Port" -ForegroundColor White
Write-Host "Sau nay khi co domain, ban chi can vao IIS Manager -> Bindings -> them Host Name vao port 80/443 la xong!" -ForegroundColor White


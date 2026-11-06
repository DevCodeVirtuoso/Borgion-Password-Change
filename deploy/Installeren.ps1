# Borgion Password Change - installatie
# Uitvoeren als administrator op de werkplek (of via GPO/Intune als SYSTEM):
#   powershell -ExecutionPolicy Bypass -File .\Installeren.ps1
#   powershell -ExecutionPolicy Bypass -File .\Installeren.ps1 -GeenBureaublad   (alleen Startmenu)
param([switch]$GeenBureaublad)

$ErrorActionPreference = 'Stop'
$name    = 'Borgion Password Change'
$exeName = 'BorgionPasswordChange.exe'
$target  = Join-Path $env:ProgramFiles 'Borgion\PasswordChange'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Start dit script als administrator.'
}

$source = Join-Path $PSScriptRoot $exeName
if (-not (Test-Path $source)) { throw "$exeName niet gevonden naast dit script." }

# .NET Framework 4.8 controleren (standaard aanwezig op Windows 10 1903+ en Windows 11)
$release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
if (-not $release -or $release -lt 528040) {
    Write-Warning '.NET Framework 4.8 lijkt niet geinstalleerd. Installeer dit eerst: https://go.microsoft.com/fwlink/?linkid=2088631'
}

Get-Process -Name 'BorgionPasswordChange' -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $source $target -Force
$config = Join-Path $PSScriptRoot "$exeName.config"
$targetConfig = Join-Path $target "$exeName.config"
# Bestaande (aangepaste) configuratie niet overschrijven
if ((Test-Path $config) -and -not (Test-Path $targetConfig)) { Copy-Item $config $target }
$exe = Join-Path $target $exeName

$shell = New-Object -ComObject WScript.Shell
$dirs = @(Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs')
if (-not $GeenBureaublad) { $dirs += [Environment]::GetFolderPath('CommonDesktopDirectory') }
foreach ($dir in $dirs) {
    $lnk = $shell.CreateShortcut((Join-Path $dir "$name.lnk"))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $target
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'Wijzig je Windows-wachtwoord'
    $lnk.Save()
}

Write-Host "Geinstalleerd in $target" -ForegroundColor Green
Write-Host 'Snelkoppeling aangemaakt in het Startmenu' -NoNewline -ForegroundColor Green
if (-not $GeenBureaublad) { Write-Host ' en op het bureaublad (alle gebruikers).' -ForegroundColor Green } else { Write-Host '.' -ForegroundColor Green }

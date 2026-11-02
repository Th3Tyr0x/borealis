# Installs Borealis from dist\Borealis to "C:\Program Files\Borealis" (admin): service and tray app autostart.
# Settings and profiles in C:\ProgramData\Borealis are kept; a running service or app is stopped first.
# Installiert Borealis aus dist\Borealis nach "C:\Program Files\Borealis" (Admin): Dienst und Autostart der App.
# Einstellungen und Profile in C:\ProgramData\Borealis bleiben; laufender Dienst oder App wird vorher beendet.
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\dist\Borealis'),
    [string]$Target = (Join-Path $env:ProgramFiles 'Borealis'),
    [string]$LogFile
)

$ErrorActionPreference = 'Stop'
function Say([string]$text) { Write-Host $text; if ($LogFile) { $text | Out-File -Encoding utf8 -Append $LogFile } }

try {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { throw 'Bitte als Administrator ausführen.' }
    $Source = [IO.Path]::GetFullPath($Source)
    if (-not (Test-Path (Join-Path $Source 'Borealis.Service.exe'))) { throw "Kein Build in $Source (erst setup\build.ps1 ausführen)." }

    # Stop old instances: installed service, test service, app
    # Alte Instanzen beenden: installierter Dienst, Testdienst, App
    if (Get-Service BorealisService -ErrorAction SilentlyContinue) {
        Say 'Vorhandenen Dienst anhalten ...'
        Stop-Service BorealisService -Force -ErrorAction SilentlyContinue
    }
    Get-Process Borealis.Service, Borealis -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800

    Say "Dateien nach $Target kopieren ..."
    New-Item -ItemType Directory -Force $Target | Out-Null
    Copy-Item (Join-Path $Source '*') $Target -Recurse -Force

    Say 'Dienst einrichten und starten ...'
    & (Join-Path $Target 'Borealis.Service.exe') install | ForEach-Object { Say $_ }
    if ($LASTEXITCODE -ne 0) { throw "Dienst nicht eingerichtet (Code $LASTEXITCODE)." }

    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty $run -Name Borealis -Value ('"{0}" --minimized' -f (Join-Path $Target 'Borealis.exe'))
    # Without this entry Task Manager hides the app from startup apps (02 = enabled)
    # Ohne diesen Eintrag fehlt die App im Task-Manager unter Autostart (02 = an)
    $approved = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
    if (-not (Test-Path $approved)) { New-Item $approved | Out-Null }
    Set-ItemProperty $approved -Name Borealis -Type Binary -Value ([byte[]](2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))
    Say 'Autostart der App eingetragen.'
    Say 'Fertig.'
}
catch {
    Say "FEHLER: $($_.Exception.Message)"
    exit 1
}

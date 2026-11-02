# Installiert Borealis aus dist\Borealis nach "C:\Program Files\Borealis" (Administrator):
#  - Dienst BorealisService (LocalSystem, startet mit Windows, Neustart nach Absturz)
#  - Tray-App startet bei der Anmeldung des ausführenden Benutzers (HKCU\...\Run, abschaltbar in den Einstellungen)
# Einstellungen und Profile in C:\ProgramData\Borealis bleiben erhalten. Ein laufender Borealis-Dienst oder eine
# laufende App (auch aus einem Entwicklungsordner) wird vorher beendet.
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

    # Alte Instanzen beenden: installierter Dienst, Testbetrieb-Dienst, App
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
    # Ohne diesen Eintrag zeigt der Task-Manager die App nicht unter „Autostart-Apps“ (02 = aktiviert)
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

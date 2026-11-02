# Entfernt Borealis (Administrator): Dienst, Autostart der App und "C:\Program Files\Borealis".
# Einstellungen und Profile in C:\ProgramData\Borealis bleiben, außer mit -RemoveSettings.
# Die Lüfterkurven im Chip bleiben bis zum nächsten Neustart, danach regelt das BIOS. Soll wieder der
# ASUS-Lüfterdienst regeln, vorher in Borealis die Lüftersteuerung ausschalten (Borealis gibt die Lüfter dann zurück
# und erlaubt den Dienst wieder).
param(
    [string]$Target = (Join-Path $env:ProgramFiles 'Borealis'),
    [switch]$RemoveSettings,
    [string]$LogFile
)

$ErrorActionPreference = 'Stop'
function Say([string]$text) { Write-Host $text; if ($LogFile) { $text | Out-File -Encoding utf8 -Append $LogFile } }

try {
    $service = Join-Path $Target 'Borealis.Service.exe'
    if (Test-Path $service) { & $service uninstall | ForEach-Object { Say $_ } }
    Get-Process Borealis.Service, Borealis -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Borealis -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    if (Test-Path $Target) { Remove-Item $Target -Recurse -Force }
    if ($RemoveSettings -and (Test-Path "$env:ProgramData\Borealis")) { Remove-Item "$env:ProgramData\Borealis" -Recurse -Force }
    Say 'Borealis entfernt.'
}
catch {
    Say "FEHLER: $($_.Exception.Message)"
    exit 1
}

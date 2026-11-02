# Removes Borealis (admin): service, app autostart, program folder. Settings stay unless -RemoveSettings.
# Fan curves stay in the chip until reboot. To hand fans back to ASUS, turn off fan control in Borealis first.
# Entfernt Borealis (Admin): Dienst, Autostart, Programmordner. Einstellungen bleiben, außer mit -RemoveSettings.
# Lüfterkurven bleiben bis zum Neustart im Chip. Zurück an ASUS: vorher die Lüftersteuerung in Borealis ausschalten.
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

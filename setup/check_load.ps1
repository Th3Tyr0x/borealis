# Ladetest vor Installation oder Release: Lässt Windows (Smart App Control, App-Steuerung) die gebauten Dateien
# in dist\Borealis laden? Ohne Adminrechte, ändert nichts. Unsignierte Builds bewertet Smart App Control je Datei neu;
# wird etwas blockiert, hilft oft schon ein Build mit leicht geändertem Inhalt.
param([string]$Dist = (Join-Path $PSScriptRoot '..\dist\Borealis'))

$start = Get-Date
$ok = $true
try { $v = & (Join-Path $Dist 'Borealis.Service.exe') --version 2>&1; "Dienst: $v" } catch { 'Dienst: BLOCKIERT'; $ok = $false }
$out = Join-Path $env:TEMP 'borealis_effecttest.txt'
Remove-Item $out -ErrorAction SilentlyContinue
$p = Start-Process (Join-Path $Dist 'Borealis.exe') -ArgumentList '--effecttest', "`"$out`"" -PassThru
$p.WaitForExit(60000) | Out-Null
"App: Exit $($p.ExitCode), Ausgabe $(Test-Path $out)"
Start-Sleep -Seconds 2
$blocked = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; StartTime = $start; Id = 3077 } -ErrorAction SilentlyContinue |
    ForEach-Object { if ($_.Message -match 'load (\\Device\\\S+)') { Split-Path $Matches[1] -Leaf } } |
    Where-Object { $_ -like 'Borealis*' } | Sort-Object -Unique
if ($blocked) { "Blockiert: $($blocked -join ', ')"; $ok = $false }
if ($ok) { 'ERGEBNIS: alles erlaubt' } else { 'ERGEBNIS: blockiert'; exit 1 }

# Builds Borealis and the installer dist\Borealis-Setup-<version>.exe with Inno Setup 7 (must be installed).
# -CertThumbprint signs program files, installer and uninstaller (setup\sign.ps1).
# Baut Borealis und den Installer dist\Borealis-Setup-<Version>.exe mit Inno Setup 7 (muss installiert sein).
# -CertThumbprint signiert Programmdateien, Installer und Deinstaller (setup\sign.ps1).
param(
    [switch]$SkipBuild,
    [string]$CertThumbprint,
    [switch]$AllowUntrusted
)

$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -CertThumbprint $CertThumbprint -AllowUntrusted:$AllowUntrusted }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 7 (ISCC.exe) nicht gefunden.' }

$arguments = @('/Qp')
if ($CertThumbprint) {
    # Inno Setup calls the sign script for setup and uninstaller; $f = file to sign
    # Inno Setup ruft das Signier-Skript für Setup und Deinstaller auf; $f = zu signierende Datei
    $sign = Join-Path $PSScriptRoot 'sign.ps1'
    $untrusted = if ($AllowUntrusted) { ' -AllowUntrusted' } else { '' }
    $arguments += "/DSignThumbprint=$CertThumbprint"
    $arguments += "/Sborealis=powershell.exe -NoProfile -ExecutionPolicy Bypass -File `$q$sign`$q -Thumbprint $CertThumbprint -Path `$f$untrusted"
}
& $iscc @arguments (Join-Path $PSScriptRoot 'Borealis.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup meldet Fehler (Code $LASTEXITCODE)." }
Get-ChildItem (Join-Path $PSScriptRoot '..\dist') -Filter 'Borealis-Setup-*.exe' | Sort-Object LastWriteTime | Select-Object -Last 1 |
    ForEach-Object { Write-Host ("Installer: {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }

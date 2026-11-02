# Signiert Dateien (Authenticode, SHA-256, mit Zeitstempel) mit einem Code-Signing-Zertifikat aus dem Windows-
# Zertifikatspeicher. Das geht auch mit Cloud- oder Smartcard-Zertifikaten (z. B. Certum SimplySign), solange das
# Zertifikat in "Eigene Zertifikate" erscheint; Windows fragt dann ggf. nach der PIN.
# Ohne Windows SDK (kein signtool nötig). Aufgerufen von build.ps1 und vom Installer-Bau (Inno Setup, SignTool).
param(
    [Parameter(Mandatory)][string]$Thumbprint,
    [Parameter(Mandatory)][string[]]$Path,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    # Nur zum Testen mit einem selbst erstellten Zertifikat: Signatur auch ohne vertrauenswürdige Kette akzeptieren
    [switch]$AllowUntrusted
)

$ErrorActionPreference = 'Stop'
$cert = Get-ChildItem "Cert:\CurrentUser\My\$Thumbprint", "Cert:\LocalMachine\My\$Thumbprint" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $cert) { throw "Zertifikat $Thumbprint nicht gefunden (Eigene Zertifikate)." }
if (-not ($cert.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')) { throw "Zertifikat $Thumbprint ist nicht für Code-Signatur." }

foreach ($file in $Path) {
    $result = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampServer -IncludeChain NotRoot
    $ok = $result.Status -eq 'Valid' -or ($AllowUntrusted -and $result.SignerCertificate -and $result.Status -eq 'UnknownError')
    if (-not $ok) { throw "Signieren fehlgeschlagen: $file ($($result.Status): $($result.StatusMessage))" }
    Write-Host "Signiert: $file ($($result.Status))"
}

# Signs files (Authenticode, SHA-256, timestamp) with a code signing certificate from the Windows store,
# also cloud or smartcard certificates (e.g. Certum SimplySign). No Windows SDK needed.
# Signiert Dateien (Authenticode, SHA-256, Zeitstempel) mit einem Code-Signing-Zertifikat aus dem Windows-Speicher,
# auch Cloud- oder Smartcard-Zertifikate (z. B. Certum SimplySign). Ohne Windows SDK.
param(
    [Parameter(Mandatory)][string]$Thumbprint,
    [Parameter(Mandatory)][string[]]$Path,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    # Testing with a self-made certificate only: accept an untrusted chain
    # Nur zum Testen mit eigenem Zertifikat: nicht vertrauenswürdige Kette akzeptieren
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

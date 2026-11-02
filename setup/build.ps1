# Builds Borealis (Release) into dist\Borealis: Borealis.exe and Borealis.Service.exe. Needs the .NET 10 SDK.
# The CLI is left out (borealis.exe would clash with Borealis.exe). -CertThumbprint signs all own files.
# Baut Borealis (Release) nach dist\Borealis: Borealis.exe und Borealis.Service.exe. Braucht das .NET-10-SDK.
# Ohne Kommandozeile (borealis.exe kollidiert mit Borealis.exe). -CertThumbprint signiert alle eigenen Dateien.
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\dist\Borealis'),
    [string]$CertThumbprint,
    [switch]$AllowUntrusted
)

$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot '..\src'
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
foreach ($project in 'Borealis.App\Borealis.App.csproj', 'Borealis.Service\Borealis.Service.csproj') {
    dotnet publish (Join-Path $src $project) -c Release -o $Output --no-self-contained -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { throw "Build von $project fehlgeschlagen." }
}
if ($CertThumbprint) {
    $files = Get-ChildItem $Output -File | Where-Object { $_.Name -like 'Borealis*' -and $_.Extension -in '.exe', '.dll' } | ForEach-Object FullName
    & (Join-Path $PSScriptRoot 'sign.ps1') -Thumbprint $CertThumbprint -Path $files -AllowUntrusted:$AllowUntrusted
}
Write-Host "Fertig: $Output"

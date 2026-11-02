# Baut Borealis (Release) nach dist\Borealis: Tray-App Borealis.exe und Dienst Borealis.Service.exe im selben Ordner.
# Braucht das .NET-10-SDK; auf dem Zielrechner muss die .NET-10-Desktop-Laufzeit installiert sein.
# Die Kommandozeile (borealis.exe) gehört nicht dazu: Sie hieße unter Windows gleich wie Borealis.exe.
# Mit -CertThumbprint werden alle eigenen .exe/.dll signiert (setup\sign.ps1).
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

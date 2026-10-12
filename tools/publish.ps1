<#
.SYNOPSIS
  Builds Tumbler for Windows and signs it.

.EXAMPLE
  # Self-signed certificate made with make-test-certificate.ps1, or any
  # certificate already in your Windows certificate store:
  .\tools\publish.ps1 -Thumbprint 1A2B3C...

  # A purchased certificate supplied as a .pfx file:
  .\tools\publish.ps1 -PfxPath C:\certs\mycompany.pfx

  # Easiest: after running make-test-certificate.ps1, just
  .\tools\publish.ps1

  Output goes to .\publish  (and .\Tumbler-win-x64.zip).
#>
param(
    [string]$Thumbprint,
    [string]$PfxPath,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish'),
    # Include .NET itself, so the PC running Tumbler needs nothing installed (used for the installer).
    [switch]$SelfContained,
    [switch]$NoZip
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Write-Host "Building..." -ForegroundColor Cyan
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
$sc = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish (Join-Path $root 'src\Tumbler\Tumbler.vbproj') -c Release -r win-x64 --self-contained $sc -o $Output
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Get-ChildItem $Output -Filter *.pdb | Remove-Item

# Find the certificate.
$cert = $null
if ($PfxPath) {
    $password = Read-Host "Password for $(Split-Path $PfxPath -Leaf)" -AsSecureString
    # Works in both Windows PowerShell 5.1 and PowerShell 7.
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path $PfxPath).Path, $password)
} elseif (-not $Thumbprint) {
    # No thumbprint given: use the certificate made by make-test-certificate.ps1, if there is one.
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object FriendlyName -like 'Tumbler code signing*' |
            Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $cert) {
        throw "No Tumbler certificate found. Run tools\make-test-certificate.ps1 first, or pass -Thumbprint / -PfxPath."
    }
} else {
    $clean = $Thumbprint -replace '\s', ''
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert |
            Where-Object Thumbprint -eq $clean | Select-Object -First 1
    if (-not $cert) { throw "No code-signing certificate with thumbprint $clean in your certificate store." }
}

if ($cert) {
    Write-Host "Signing with: $($cert.Subject)" -ForegroundColor Cyan
    # Sign our own files. Third-party DLLs (PDFsharp, PDFium) keep their own signatures or none.
    $files = 'Tumbler.exe', 'Tumbler.dll', 'Tumbler.Core.dll' | ForEach-Object { Join-Path $Output $_ }
    foreach ($f in $files) {
        $result = Set-AuthenticodeSignature -FilePath $f -Certificate $cert -HashAlgorithm SHA256 `
                    -TimestampServer $TimestampServer -IncludeChain All
        # A self-signed certificate reports UnknownError until it is trusted; the file is still signed.
        if (-not $result.SignerCertificate) { throw "Signing $f failed: $($result.StatusMessage)" }
        Write-Host ("  {0,-18} {1}" -f (Split-Path $f -Leaf), $result.Status)
    }
}

if ($NoZip) {
    Write-Host "Done: $Output" -ForegroundColor Green
    return
}
$zip = Join-Path $root 'Tumbler-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $zip
Write-Host ""
Write-Host "Done: $Output" -ForegroundColor Green
Write-Host "Zip:  $zip"

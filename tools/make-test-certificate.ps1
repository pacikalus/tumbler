<#
.SYNOPSIS
  Creates a self-signed code-signing certificate and trusts it on THIS PC only.

.DESCRIPTION
  After this, Tumbler builds signed with publish.ps1 show your name as the
  publisher on this computer. Other computers still say "Unknown publisher"
  unless you install Tumbler-signing.cer on them too (see README).

  Run it once, in a normal (not admin) PowerShell window:
      .\tools\make-test-certificate.ps1 -Name "Mike"
  Windows will ask you to confirm adding the certificate. Click Yes.
#>
param(
    [string]$Name = $env:USERNAME,
    [int]$Years = 5
)
$ErrorActionPreference = 'Stop'

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=$Name" `
    -FriendlyName "Tumbler code signing ($Name)" `
    -CertStoreLocation Cert:\CurrentUser\My `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($Years)

# Export the public part so it can be trusted (here, and on other PCs if wanted).
$cerPath = Join-Path $PSScriptRoot 'Tumbler-signing.cer'
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

# Trust it on this PC: as a root (so the chain is valid) and as a trusted publisher.
foreach ($store in 'Root', 'TrustedPublisher') {
    $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($store, 'CurrentUser')
    $s.Open('ReadWrite')
    $s.Add($cert)
    $s.Close()
}

Write-Host ""
Write-Host "Certificate created for '$Name'." -ForegroundColor Green
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host "Public certificate saved to: $cerPath"
Write-Host ""
Write-Host "Next, build and sign Tumbler with:"
Write-Host "  powershell -ExecutionPolicy Bypass -File tools\publish.ps1"

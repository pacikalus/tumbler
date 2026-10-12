# Called by "Build and Sign Tumbler.bat". Makes a signing certificate the first
# time, then builds and signs Tumbler, then opens the finished folder.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET 8 SDK is not installed. Get it from https://dotnet.microsoft.com/download/dotnet/8.0 (choose 'SDK x64'), then run this again."
    }

    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object FriendlyName -like 'Tumbler code signing*' | Select-Object -First 1
    if (-not $cert) {
        Write-Host "First run: creating your signing certificate." -ForegroundColor Cyan
        Write-Host "Windows will ask to install a certificate. Click YES." -ForegroundColor Yellow
        & (Join-Path $PSScriptRoot 'make-test-certificate.ps1') -Name $env:USERNAME
    }

    & (Join-Path $PSScriptRoot 'publish.ps1')

    Write-Host ""
    Write-Host "All done. Opening the folder with Tumbler.exe ..." -ForegroundColor Green
    Start-Process explorer.exe (Join-Path $root 'publish')
}
catch {
    Write-Host ""
    Write-Host "Something went wrong:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

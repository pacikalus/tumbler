# Called by "Make Installer.bat". Builds Tumbler with .NET included (so the
# people you give it to don't need to install anything else), signs it, wraps
# it in a setup program with Inno Setup, signs that, and opens the dist folder.
#   -Publish  publishes it on GitHub without asking.
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Find-InnoCompiler {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
    $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET 8 SDK is not installed. Get it from https://dotnet.microsoft.com/download/dotnet/8.0 (choose 'SDK x64'), then run this again."
    }

    # Inno Setup makes the setup program. Install it the first time (free, from jrsoftware.org).
    $iscc = Find-InnoCompiler
    if (-not $iscc) {
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            throw "Inno Setup 6 is needed to make the installer. Install it from https://jrsoftware.org/isdl.php, then run this again."
        }
        Write-Host "First run: installing Inno Setup (makes the setup program)..." -ForegroundColor Cyan
        Write-Host "If Windows asks for permission, click YES." -ForegroundColor Yellow
        winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements
        $iscc = Find-InnoCompiler
        if (-not $iscc) { throw "Inno Setup did not install. Install it from https://jrsoftware.org/isdl.php, then run this again." }
    }

    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object FriendlyName -like 'Tumbler code signing*' | Select-Object -First 1
    if (-not $cert) {
        Write-Host "First run: creating your signing certificate." -ForegroundColor Cyan
        Write-Host "Windows will ask to install a certificate. Click YES." -ForegroundColor Yellow
        & (Join-Path $PSScriptRoot 'make-test-certificate.ps1') -Name $env:USERNAME
    }

    $appDir = Join-Path $root 'publish-installer'
    & (Join-Path $PSScriptRoot 'publish.ps1') -Output $appDir -SelfContained -NoZip

    # The build just raised the version in src\Tumbler\version.txt; read the new one.
    $version = (Get-Content (Join-Path $root 'src\Tumbler\version.txt') -Raw).Trim()
    if (-not $version) { $version = '1.0.0' }

    Write-Host ""
    Write-Host "Making the setup program for Tumbler $version ..." -ForegroundColor Cyan
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$appDir" (Join-Path $root 'installer\Tumbler.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup could not make the installer." }

    $setup = Join-Path $root "dist\Tumbler-Setup-$version.exe"
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object FriendlyName -like 'Tumbler code signing*' |
            Sort-Object NotAfter -Descending | Select-Object -First 1
    $result = Set-AuthenticodeSignature -FilePath $setup -Certificate $cert -HashAlgorithm SHA256 `
                -TimestampServer 'http://timestamp.digicert.com' -IncludeChain All
    if (-not $result.SignerCertificate) { throw "Signing the installer failed: $($result.StatusMessage)" }

    Write-Host ""
    Write-Host "All done: $setup" -ForegroundColor Green
    Write-Host "Give people this one file. It installs Tumbler with everything it needs."

    # Publishing puts the setup on GitHub as release v<version>. Every installed
    # Tumbler checks for the newest release and offers to update itself.
    Write-Host ""
    $answer = if ($Publish) { 'Y' } else { Read-Host "Publish Tumbler $version on GitHub so installed copies can update? (Y/N)" }
    if ($answer -match '^[Yy]') {
        & (Join-Path $PSScriptRoot 'publish-release.ps1') -Version $version -Setup $setup
    } else {
        Write-Host "Not published. Run tools\publish-release.ps1 later to publish it." -ForegroundColor Yellow
    }
    Start-Process explorer.exe "/select,`"$setup`""
}
catch {
    Write-Host ""
    Write-Host "Something went wrong:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

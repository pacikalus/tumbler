# Publishes a Tumbler setup program on GitHub as release v<version>, which is
# what installed copies of Tumbler look for when they check for updates.
# Normally run by "Make Installer.bat"; can also be run on its own:
#   .\tools\publish-release.ps1                      (newest setup in dist\)
#   .\tools\publish-release.ps1 -Version 1.4.2 -Setup dist\Tumbler-Setup-1.4.2.exe
param(
    [string]$Version,
    [string]$Setup,
    [string]$Repo = 'pacikalus/tumbler'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $Setup) {
    $newest = Get-ChildItem (Join-Path $root 'dist') -Filter 'Tumbler-Setup-*.exe' -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { throw "No setup program in dist\. Run ""Make Installer.bat"" first." }
    $Setup = $newest.FullName
}
if (-not $Version) {
    if ((Split-Path $Setup -Leaf) -match 'Tumbler-Setup-(\d+\.\d+\.\d+)\.exe') { $Version = $Matches[1] }
    else { throw "Can't tell the version from $Setup; pass -Version." }
}
$tag = "v$Version"

# GitHub's command-line tool does the upload. Install it the first time.
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "Publishing needs the GitHub CLI. Install it from https://cli.github.com, then run this again."
    }
    Write-Host "First run: installing the GitHub command-line tool..." -ForegroundColor Cyan
    winget install --id GitHub.cli --exact --silent --accept-package-agreements --accept-source-agreements
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "The GitHub CLI did not install. Get it from https://cli.github.com." }
}
gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Sign in to GitHub once (a browser window opens):" -ForegroundColor Yellow
    gh auth login --hostname github.com --web --git-protocol https
    if ($LASTEXITCODE -ne 0) { throw "GitHub sign-in did not finish." }
}

gh release view $tag --repo $Repo *> $null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Release $tag already exists; replacing its setup program." -ForegroundColor Cyan
    gh release upload $tag $Setup --repo $Repo --clobber
} else {
    Write-Host "Publishing $tag ..." -ForegroundColor Cyan
    # "What's new" = the commit titles since the previous release. Tumbler shows
    # these in its update window; edit them on GitHub afterwards if you like.
    $previous = gh release list --repo $Repo --limit 1 --json tagName --jq '.[0].tagName' 2>$null
    $lines = @()
    if ($previous) {
        $compare = (gh api "repos/$Repo/compare/$previous...main" 2>$null | Out-String) | ConvertFrom-Json
        $lines = @($compare.commits | ForEach-Object { ($_.commit.message -split "`n")[0].Trim() } |
                   Where-Object { $_ -and $_ -notmatch '^Version \d' } | ForEach-Object { "- $_" })
    }
    if (-not $lines) { $lines = @('- First release with automatic updates.') }
    $notes = Join-Path $env:TEMP "tumbler-release-notes.md"
    Set-Content -Path $notes -Value $lines -Encoding UTF8
    gh release create $tag $Setup --repo $Repo --target main --title "Tumbler $Version" --notes-file $notes --latest
}
if ($LASTEXITCODE -ne 0) { throw "Publishing to GitHub failed." }
Write-Host "Published: https://github.com/$Repo/releases/tag/$tag" -ForegroundColor Green

@echo off
rem Double-click me: publishes the newest setup in dist\ on GitHub, so installed copies of Tumbler can update.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\publish-release.ps1"
if errorlevel 1 (
    echo.
    echo This window closes in 15 seconds...
    timeout /t 15 /nobreak >nul
    exit /b 1
)
echo.
echo This window closes in 4 seconds...
timeout /t 4 /nobreak >nul

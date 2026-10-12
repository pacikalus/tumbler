@echo off
rem Double-click me: makes the installer and publishes it on GitHub without asking.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\build-installer.ps1" -Publish
if errorlevel 1 (
    echo.
    echo This window closes in 30 seconds...
    timeout /t 30 /nobreak >nul
    exit /b 1
)
echo.
echo This window closes in 10 seconds...
timeout /t 10 /nobreak >nul

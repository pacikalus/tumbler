@echo off
rem Double-click me: builds Tumbler and signs it with your own certificate.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\build-and-sign.ps1"
if errorlevel 1 (
    echo.
    rem On failure, wait longer so the message can be read.
    echo This window closes in 15 seconds...
    timeout /t 15 /nobreak >nul
    exit /b 1
)
echo.
echo This window closes in 4 seconds...
timeout /t 4 /nobreak >nul

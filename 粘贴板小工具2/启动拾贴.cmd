@echo off
setlocal
set "APP_DIR=%~dp0"
if not exist "%APP_DIR%dist\QuietClip.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%APP_DIR%build.ps1"
    if errorlevel 1 (
        pause
        exit /b 1
    )
)
start "" "%APP_DIR%dist\QuietClip.exe"
endlocal

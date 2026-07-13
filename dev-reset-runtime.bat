@echo off
setlocal EnableExtensions

cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-reset-runtime.ps1"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
	echo.
	echo [Nexus] Runtime reset failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%

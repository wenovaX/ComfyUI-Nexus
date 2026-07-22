@echo off
setlocal EnableExtensions

for %%I in ("%~dp0\..\..") do set "PROJECT_ROOT=%%~fI"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-reset-runtime.ps1" -ProjectRoot "%PROJECT_ROOT%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
	echo.
	echo [Nexus] Runtime reset failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%

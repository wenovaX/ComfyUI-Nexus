@echo off
setlocal EnableExtensions

for %%I in ("%~dp0\..\..") do set "PROJECT_ROOT=%%~fI"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-set-version.ps1" -ProjectRoot "%PROJECT_ROOT%"
exit /b %ERRORLEVEL%

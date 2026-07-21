@echo off
setlocal EnableExtensions

cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-set-version.ps1"
exit /b %ERRORLEVEL%

@echo off
setlocal EnableExtensions

if /I "%~1"=="open" (
	endlocal
	cd /d "%~dp0tools\windows"
	exit /b 0
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\windows\dev-help.ps1" %*
exit /b %ERRORLEVEL%

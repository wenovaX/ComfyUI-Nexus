@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "PROJECT=ComfyUI-Nexus.csproj"
set "FRAMEWORK=net10.0-windows10.0.19041.0"
set "CONFIGURATION=Release"
if not "%~1"=="" set "CONFIGURATION=%~1"
set "CLEAN_DOTNET_OUTPUTS=false"
set "PACKAGE_MODE="
if /I "%~2"=="clean" set "CLEAN_DOTNET_OUTPUTS=true"
if /I "%~2"=="folder" set "PACKAGE_MODE=folder"
if /I "%~2"=="single" set "PACKAGE_MODE=single"
if /I "%~3"=="clean" set "CLEAN_DOTNET_OUTPUTS=true"
if /I "%~3"=="folder" set "PACKAGE_MODE=folder"
if /I "%~3"=="single" set "PACKAGE_MODE=single"
if "%PACKAGE_MODE%"=="" (
    echo.
    echo [Nexus] ERROR: package mode is required.
    echo.
    echo Usage:
    echo   build-as-binary.bat [configuration] [folder^|single] [clean]
    echo.
    echo Examples:
    echo   build-as-binary.bat Release folder
    echo   build-as-binary.bat Release single
    echo   build-as-binary.bat Release folder clean
    echo   build-as-binary.bat Release single clean
    echo.
    exit /b 1
)
set "PACKAGE_MODE_SINGLE=false"
if /I "%PACKAGE_MODE%"=="single" set "PACKAGE_MODE_SINGLE=true"
set "RUNTIME=win-x64"
set "PUBLISH_ROOT=%CD%\artifacts\binary"
if /I "%PACKAGE_MODE%"=="single" (
    set "PUBLISH_DIR=%PUBLISH_ROOT%\ComfyUI-Nexus-%RUNTIME%"
) else (
    set "PUBLISH_DIR=%PUBLISH_ROOT%\App"
)
set "PUBLISH_EXE=%PUBLISH_DIR%\ComfyUI-Nexus.exe"
for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "BUILD_TIMESTAMP=%%I"
for /f %%I in ('powershell -NoProfile -Command "$props = [xml](Get-Content -Raw -Path 'Directory.Build.props'); $version = $props.Project.PropertyGroup.NexusVersion | Select-Object -First 1; if ([string]::IsNullOrWhiteSpace($version)) { '0.0.0' } else { $version }"') do set "APP_VERSION=%%I"
set "BUILD_ROOT=%CD%\build"
set "RELEASE_DIR=%BUILD_ROOT%\%CONFIGURATION%_%BUILD_TIMESTAMP%"
set "RELEASE_EXE=%RELEASE_DIR%\ComfyUI-Nexus.exe"
if /I "%PACKAGE_MODE%"=="single" (
    set "ARCHIVE_NAME=ComfyUI-Nexus-v%APP_VERSION%-%RUNTIME%-portable-single-%BUILD_TIMESTAMP%.zip"
) else (
    set "ARCHIVE_NAME=ComfyUI-Nexus-v%APP_VERSION%-%RUNTIME%-portable-folder-%BUILD_TIMESTAMP%.zip"
)
set "ARCHIVE_PATH=%RELEASE_DIR%\%ARCHIVE_NAME%"

echo.
echo [Nexus] Building ComfyUI-Nexus portable release
echo [Nexus] Project       : %PROJECT%
echo [Nexus] Framework     : %FRAMEWORK%
echo [Nexus] Runtime       : %RUNTIME%
echo [Nexus] Configuration : %CONFIGURATION%
echo [Nexus] Package mode  : %PACKAGE_MODE%
echo [Nexus] Clean bin/obj : %CLEAN_DOTNET_OUTPUTS%
echo [Nexus] Version       : %APP_VERSION%
echo [Nexus] Publish temp  : %PUBLISH_DIR%
echo [Nexus] Release folder: %RELEASE_DIR%
echo [Nexus] Release zip   : %ARCHIVE_PATH%
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [Nexus] ERROR: dotnet SDK was not found in PATH.
    exit /b 1
)

if not exist "%PROJECT%" (
    echo [Nexus] ERROR: %PROJECT% was not found.
    exit /b 1
)

if /I "%CLEAN_DOTNET_OUTPUTS%"=="true" (
    echo [Nexus] Cleaning previous dotnet build outputs...
    dotnet clean "%PROJECT%" ^
        -f "%FRAMEWORK%" ^
        -c "%CONFIGURATION%" ^
        -p:RuntimeIdentifier="%RUNTIME%" ^
        -p:WindowsPackageType=None ^
        -v quiet
    if errorlevel 1 (
        echo [Nexus] ERROR: clean failed.
        exit /b 1
    )
)

echo [Nexus] Cleaning previous release staging outputs...
if exist "%PUBLISH_ROOT%" rmdir /s /q "%PUBLISH_ROOT%"
if exist "%RELEASE_DIR%" rmdir /s /q "%RELEASE_DIR%"
if not exist "%PUBLISH_ROOT%" mkdir "%PUBLISH_ROOT%"

echo.
echo [Nexus] Restoring packages...
dotnet restore "%PROJECT%" ^
    -p:TargetFramework="%FRAMEWORK%" ^
    -p:RuntimeIdentifier="%RUNTIME%" ^
    -p:UseMonoRuntime=false ^
    -p:WindowsPackageType=None ^
    -p:Deterministic=true ^
    -p:ContinuousIntegrationBuild=true ^
    -p:PathMap="%CD%=/_/ComfyUI-Nexus"
if errorlevel 1 (
    echo [Nexus] ERROR: restore failed.
    exit /b 1
)

echo.
if /I "%PACKAGE_MODE%"=="single" (
    echo [Nexus] Publishing compact single-file Windows app...
) else (
    echo [Nexus] Publishing self-contained Windows app folder...
)
dotnet publish "%PROJECT%" ^
    -f "%FRAMEWORK%" ^
    -c "%CONFIGURATION%" ^
    -r "%RUNTIME%" ^
    --self-contained true ^
    -o "%PUBLISH_DIR%" ^
    -p:UseMonoRuntime=false ^
    -p:WindowsPackageType=None ^
    -p:WindowsAppSDKSelfContained=true ^
    -p:PublishSingleFile=%PACKAGE_MODE_SINGLE% ^
    -p:IncludeNativeLibrariesForSelfExtract=%PACKAGE_MODE_SINGLE% ^
    -p:EnableCompressionInSingleFile=false ^
    -p:SatelliteResourceLanguages=en%%3Bko%%3Bzh-Hans%%3Bzh-Hant ^
    -p:PublishReadyToRun=false ^
    -p:Deterministic=true ^
    -p:ContinuousIntegrationBuild=true ^
    -p:PathMap="%CD%=/_/ComfyUI-Nexus" ^
    -p:DebugType=None ^
    -p:DebugSymbols=false
if errorlevel 1 (
    echo [Nexus] ERROR: publish failed.
    exit /b 1
)

if not exist "%PUBLISH_EXE%" (
    echo [Nexus] ERROR: expected executable was not produced:
    echo [Nexus]        %PUBLISH_EXE%
    exit /b 1
)

echo.
echo [Nexus] Packing minimal portable release folder...
powershell -NoProfile -ExecutionPolicy Bypass -File "%CD%\build-portable-release.ps1" ^
    -PublishDirectory "%PUBLISH_DIR%" ^
    -ProjectRoot "%CD%" ^
    -ReleaseDirectory "%RELEASE_DIR%" ^
    -ArchivePath "%ARCHIVE_PATH%" ^
    -PackageMode "%PACKAGE_MODE%"
if errorlevel 1 (
    echo [Nexus] ERROR: portable release packaging failed.
    exit /b 1
)

if not exist "%RELEASE_EXE%" (
    echo [Nexus] ERROR: release executable was not produced:
    echo [Nexus]        %RELEASE_EXE%
    exit /b 1
)

echo.
echo [Nexus] Cleaning temporary publish folder...
if exist "%PUBLISH_ROOT%" rmdir /s /q "%PUBLISH_ROOT%"

echo.
echo [Nexus] Portable release build complete.
echo [Nexus] Release folder:
echo [Nexus]   %RELEASE_DIR%
echo.
echo [Nexus] Release zip:
echo [Nexus]   %ARCHIVE_PATH%
echo.

exit /b 0

@echo off
setlocal EnableExtensions

for %%I in ("%~dp0\..\..") do set "PROJECT_ROOT=%%~fI"
cd /d "%PROJECT_ROOT%"

set "PROJECT=ComfyUI-Nexus.csproj"
set "FRAMEWORK=net10.0-windows10.0.19041.0"
set "CONFIGURATION=Release"
set "CLEAN_DOTNET_OUTPUTS=false"
set "CREATE_ARCHIVE=false"
set "PACKAGE_MODE="
set "CERT_NAME="
set "CONFIGURATION_SET=false"

:parse_arguments
if "%~1"=="" goto arguments_parsed
if /I "%~1"=="clean" (
    set "CLEAN_DOTNET_OUTPUTS=true"
    shift
    goto parse_arguments
)
if /I "%~1"=="archive" (
    set "CREATE_ARCHIVE=true"
    shift
    goto parse_arguments
)
if /I "%~1"=="zip" (
    set "CREATE_ARCHIVE=true"
    shift
    goto parse_arguments
)
if /I "%~1"=="folder" (
    set "PACKAGE_MODE=folder"
    shift
    goto parse_arguments
)
if /I "%~1"=="single" (
    set "PACKAGE_MODE=single"
    shift
    goto parse_arguments
)
if /I "%~1"=="app-store" (
    set "PACKAGE_MODE=app-store"
    shift
    goto parse_arguments
)
if /I "%~1"=="--cert" (
    if "%~2"=="" (
        echo [Nexus] ERROR: --cert requires a certificate name.
        exit /b 1
    )
    set "CERT_NAME=%~2"
    shift
    shift
    goto parse_arguments
)
if "%CONFIGURATION_SET%"=="false" (
    set "CONFIGURATION=%~1"
    set "CONFIGURATION_SET=true"
    shift
    goto parse_arguments
)
echo [Nexus] ERROR: unknown argument: %~1
exit /b 1

:arguments_parsed
if "%PACKAGE_MODE%"=="" (
    echo.
    echo [Nexus] ERROR: package mode is required.
    echo.
    echo Usage:
    echo   dev-build-as-binary.bat [configuration] [folder^|single^|app-store] [clean] [archive^|zip] [--cert name]
    echo.
    echo Examples:
    echo   dev-build-as-binary.bat Release folder
    echo   dev-build-as-binary.bat Release single
    echo   dev-build-as-binary.bat Release folder clean
    echo   dev-build-as-binary.bat Release folder archive
    echo   dev-build-as-binary.bat Release single clean
    echo   dev-build-as-binary.bat Release folder --cert develop
    echo   dev-build-as-binary.bat Release app-store clean
    echo.
    exit /b 1
)
if /I "%PACKAGE_MODE%"=="app-store" goto build_store
set "PACKAGE_MODE_SINGLE=false"
if /I "%PACKAGE_MODE%"=="single" set "PACKAGE_MODE_SINGLE=true"
set "CREATE_ARCHIVE_ARGUMENT="
if /I "%CREATE_ARCHIVE%"=="true" set "CREATE_ARCHIVE_ARGUMENT=-CreateArchive"
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
echo [Nexus] Create archive: %CREATE_ARCHIVE%
if "%CERT_NAME%"=="" (
    echo [Nexus] Code signing  : not requested
) else (
    echo [Nexus] Code signing  : %CERT_NAME%
)
echo [Nexus] Version       : %APP_VERSION%
echo [Nexus] Publish temp  : %PUBLISH_DIR%
echo [Nexus] Release folder: %RELEASE_DIR%
if /I "%CREATE_ARCHIVE%"=="true" (
    echo [Nexus] Release zip   : %ARCHIVE_PATH%
) else (
    echo [Nexus] Release zip   : skipped ^(use archive or zip^)
)
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

if not "%CERT_NAME%"=="" (
    echo [Nexus] Validating local code-signing certificate...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%\tools\windows\dev-check-code-signing-certificate.ps1" -Name "%CERT_NAME%"
    if errorlevel 1 (
        echo [Nexus] ERROR: code-signing certificate validation failed. Build was not started.
        exit /b 1
    )
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
if not "%CERT_NAME%"=="" echo [Nexus] Forwarding code-signing request to release packaging: %CERT_NAME%
powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%\tools\windows\dev-build-portable-release.ps1" ^
    -PublishDirectory "%PUBLISH_DIR%" ^
    -ProjectRoot "%CD%" ^
    -ReleaseDirectory "%RELEASE_DIR%" ^
    -ArchivePath "%ARCHIVE_PATH%" ^
    -PackageMode "%PACKAGE_MODE%" ^
    -CertificateName "%CERT_NAME%" %CREATE_ARCHIVE_ARGUMENT%
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
if /I "%CREATE_ARCHIVE%"=="true" (
    echo [Nexus] Release zip:
    echo [Nexus]   %ARCHIVE_PATH%
)
echo.

exit /b 0

:build_store
if not "%CERT_NAME%"=="" (
    echo [Nexus] ERROR: --cert is only supported for portable folder or single builds.
    exit /b 1
)
if /I "%CREATE_ARCHIVE%"=="true" (
    echo [Nexus] ERROR: archive and zip are only supported for portable folder or single builds.
    exit /b 1
)
set "STORE_CLEAN_ARGUMENT="
if /I "%CLEAN_DOTNET_OUTPUTS%"=="true" set "STORE_CLEAN_ARGUMENT=-CleanBuild"
echo.
echo [Nexus] Building Microsoft Store upload package...
powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%\tools\windows\dev-build-store-release.ps1" ^
    -Configuration "%CONFIGURATION%" ^
    -ProjectRoot "%CD%" ^
    -Framework "%FRAMEWORK%" ^
    -Runtime "win-x64" %STORE_CLEAN_ARGUMENT%
if errorlevel 1 (
    echo [Nexus] ERROR: Store package build failed.
    exit /b 1
)
exit /b 0

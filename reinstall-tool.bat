@echo off
setlocal

call :resolve_framework "%~1"
if %errorlevel% equ 2 exit /b 0
if %errorlevel% neq 0 exit /b %errorlevel%

tasklist /FI "IMAGENAME eq mux.exe" | find /I "mux.exe" >nul
if %errorlevel% equ 0 (
    echo A running mux.exe process is locking the global tool install.
    echo Exit all mux sessions and rerun reinstall-tool.bat.
    exit /b 1
)

echo Removing mux...
dotnet tool uninstall -g Mux.Cli
if %errorlevel% neq 0 (
    echo Failed to uninstall mux. Ensure no mux processes are running and rerun this script.
    exit /b %errorlevel%
)

echo Building mux for %FRAMEWORK%...
dotnet pack src\Mux.Cli\Mux.Cli.csproj --configuration Release -p:TargetFrameworks=%FRAMEWORK%
if %errorlevel% neq 0 (
    echo Build failed.
    exit /b %errorlevel%
)
echo Installing mux...
dotnet tool install -g --add-source src\Mux.Cli\bin\Release Mux.Cli
if %errorlevel% neq 0 exit /b %errorlevel%
mux -v
exit /b %errorlevel%

:resolve_framework
set "FRAMEWORK=%~1"
if /I "%FRAMEWORK%"=="/?" (
    call :usage
    exit /b 2
)
if /I "%FRAMEWORK%"=="-h" (
    call :usage
    exit /b 2
)
if /I "%FRAMEWORK%"=="--help" (
    call :usage
    exit /b 2
)

if "%FRAMEWORK%"=="" (
    dotnet --list-sdks | findstr /B /C:"10." >nul
    if %errorlevel% equ 0 (
        set "FRAMEWORK=net10.0"
    ) else (
        set "FRAMEWORK=net8.0"
    )
    exit /b 0
)

if /I "%FRAMEWORK%"=="net8" set "FRAMEWORK=net8.0"
if /I "%FRAMEWORK%"=="net8.0" exit /b 0
if /I "%FRAMEWORK%"=="net10" set "FRAMEWORK=net10.0"
if /I "%FRAMEWORK%"=="net10.0" exit /b 0

echo Unsupported framework "%~1".
echo Supported frameworks: net8.0, net10.0.
echo Use net8.0 on systems without a .NET 10 SDK.
exit /b 1

:usage
echo Usage: %~nx0 [net8.0^|net10.0]
echo Defaults to net10.0 when a .NET 10 SDK is installed, otherwise net8.0.
exit /b 0

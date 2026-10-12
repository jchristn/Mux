@echo off
REM Called within the caller's setlocal; returns TFM and FRAMEWORK_ARGS.
set "FRAMEWORK_ARGS=0"
set "TFM=%MUX_TFM%"
if /I "%~1"=="--framework" goto named
if /I "%~1"=="-f" goto named
if /I "%~1"=="-h" goto help
if /I "%~1"=="--help" goto help
if /I "%~1"=="/?" goto help
set "FRAMEWORK_FIRST=%~1"
if /I "%FRAMEWORK_FIRST:~0,3%"=="net" (
    set "TFM=%~1"
    set "FRAMEWORK_ARGS=1"
)
goto resolve
:named
if "%~2"=="" (
    echo Missing framework value after %~1. 1>&2
    exit /b 1
)
set "TFM=%~2"
set "FRAMEWORK_ARGS=2"
:resolve
if defined TFM goto validate
dotnet --list-sdks 2>nul | findstr /B /C:"10." >nul
if errorlevel 1 (set "TFM=net8.0") else (set "TFM=net10.0")
:validate
if /I "%TFM%"=="net8" set "TFM=net8.0"
if /I "%TFM%"=="net10" set "TFM=net10.0"
if /I "%TFM%"=="net8.0" exit /b 0
if /I "%TFM%"=="net10.0" exit /b 0
echo Unsupported framework "%TFM%". Supported frameworks: net8.0, net10.0. 1>&2
exit /b 1
:help
echo Usage: script [net8.0^|net10.0] [script arguments]
echo        script --framework ^<net8.0^|net10.0^> [script arguments]
echo Defaults to MUX_TFM, then net10.0 with a .NET 10 SDK, otherwise net8.0.
exit /b 2

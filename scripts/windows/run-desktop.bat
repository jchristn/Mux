@echo off
REM Launch the mux desktop application (Avalonia).
REM Usage: scripts\windows\run-desktop.bat [net8.0^|net10.0]
setlocal
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
chcp 65001 >nul
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Unexpected argument: %1. 1>&2
    exit /b 1
)
dotnet run --project "%ROOT%src\Mux.Desktop\Mux.Desktop.csproj" --framework %TFM% -p:TargetFrameworks=%TFM%
endlocal

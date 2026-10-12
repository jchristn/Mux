@echo off
REM Launch the mux system-tray agent (hosts the local REST server in the background).
REM Usage: scripts\windows\run-agent.bat [net8.0^|net10.0]
setlocal
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Unexpected argument: %1. 1>&2
    exit /b 1
)
dotnet run --project "%ROOT%src\Mux.Agent\Mux.Agent.csproj" --framework %TFM% -p:TargetFrameworks=%TFM%
endlocal

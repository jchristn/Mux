@echo off
REM Launch the mux system-tray agent (hosts the local REST server in the background).
REM Usage: scripts\windows\run-agent.bat [net8.0^|net10.0]
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
REM "call x.bat /?" shows CALL's own help, so handle /? here.
if "%~1"=="/?" (
    call "%~dp0resolve-framework.bat" --help
    exit /b 0
)
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Error: Unexpected argument: %1 1>&2
    exit /b 1
)
REM Running mux from source can start the agent (the TUI and desktop app launch it when none is running),
REM and the agent registers itself to start at login on its first run unless this marker exists. Login
REM startup is run-at-startup.bat's job, so write the marker to keep source runs from registering a build.
set "MUX_CONFIG_HOME=%MUX_CONFIG_DIR%"
if not defined MUX_CONFIG_HOME set "MUX_CONFIG_HOME=%USERPROFILE%\.mux"
if not exist "%MUX_CONFIG_HOME%" mkdir "%MUX_CONFIG_HOME%"
if not exist "%MUX_CONFIG_HOME%\startup-registered" echo registered by scripts\windows> "%MUX_CONFIG_HOME%\startup-registered"
REM The agent registers itself to start at login on its first run unless MUX_AGENT_AUTOSTART=0. Login
REM startup is run-at-startup.bat's job, so keep a source run from registering this build.
set "MUX_AGENT_AUTOSTART=0"
dotnet run --project "%ROOT%src\Mux.Agent\Mux.Agent.csproj" --framework %TFM% -p:TargetFrameworks=%TFM%
endlocal & exit /b %errorlevel%

@echo off
REM Launch the mux system-tray agent (hosts the local REST server in the background). Starts the per-user
REM install in %LOCALAPPDATA%\Programs\Mux\Agent, first installing or refreshing it when it is missing or its
REM version differs from this checkout. Set MUX_REFRESH_AGENT=1 to rebuild it anyway (to test agent changes).
REM A running agent is never replaced: a current one is reported, a stale one gets a note to quit it first.
REM This is an explicit request, so MUX_AGENT_AUTOSTART does not stop it.
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
REM Install or refresh the per-user agent only when needed; a running copy is never replaced. -Explicit makes
REM mux-apps.ps1 ignore MUX_AGENT_AUTOSTART, and it exits 3 when the per-user agent is running and stale (it has
REM printed a note about quitting it to update it).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mux-apps.ps1" -Action EnsureUserAgent -Framework %TFM% -Explicit
set "AGENT_STATUS=%errorlevel%"
if "%AGENT_STATUS%"=="3" exit /b 0
if not "%AGENT_STATUS%"=="0" (
    echo Error: Installing the tray agent failed. 1>&2
    exit /b 1
)
tasklist /FI "IMAGENAME eq Mux.Agent.exe" /FI "USERNAME eq %USERDOMAIN%\%USERNAME%" 2>nul | find /I "Mux.Agent.exe" >nul
if not errorlevel 1 (
    echo The mux agent is already running.
    exit /b 0
)
if not defined LOCALAPPDATA (
    echo Error: LOCALAPPDATA is not set, so the per-user tray agent cannot be found. 1>&2
    exit /b 1
)
set "AGENT_EXE=%LOCALAPPDATA%\Programs\Mux\Agent\Mux.Agent.exe"
if not exist "%AGENT_EXE%" (
    echo Error: The per-user tray agent is not installed. 1>&2
    exit /b 1
)
REM The agent registers itself to start at login on its first run unless MUX_AGENT_AUTOSTART=0. Login
REM startup is run-at-startup.bat's job, so keep this start from registering it.
set "MUX_AGENT_AUTOSTART=0"
start "" "%AGENT_EXE%"
echo Started the mux agent from "%AGENT_EXE%".
endlocal & exit /b 0

@echo off
REM Launch the mux desktop application (Avalonia) from source. First installs or refreshes the per-user
REM tray agent when it is missing or out of date, and starts it unless one is running.
REM Usage: scripts\windows\run-desktop.bat [net8.0^|net10.0]
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
chcp 65001 >nul
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
REM Every user surface starts the tray agent. Install or refresh the per-user agent first, but only when it
REM is missing or its version differs from this checkout (MUX_REFRESH_AGENT=1 forces it); a running copy is
REM never replaced. mux-apps.ps1 exits 2 when MUX_AGENT_AUTOSTART turns automatic agent starts off (as the
REM desktop app does); then this script does not start the agent either.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mux-apps.ps1" -Action EnsureUserAgent -Framework %TFM%
set "AGENT_STATUS=%errorlevel%"
if "%AGENT_STATUS%"=="2" goto start_surface
if not "%AGENT_STATUS%"=="0" (
    echo Error: Installing the tray agent failed. 1>&2
    exit /b 1
)
REM Start the installed agent unless one is running; the desktop app then finds it running and leaves it be.
REM MUX_AGENT_AUTOSTART=0 for the agent only keeps it from registering itself at login (run-at-startup.bat's job).
tasklist /FI "IMAGENAME eq Mux.Agent.exe" /FI "USERNAME eq %USERDOMAIN%\%USERNAME%" 2>nul | find /I "Mux.Agent.exe" >nul
if not errorlevel 1 goto start_surface
if not defined LOCALAPPDATA goto start_surface
if not exist "%LOCALAPPDATA%\Programs\Mux\Agent\Mux.Agent.exe" goto start_surface
setlocal
set "MUX_AGENT_AUTOSTART=0"
start "" "%LOCALAPPDATA%\Programs\Mux\Agent\Mux.Agent.exe"
endlocal
echo Started the tray agent.
:start_surface
dotnet run --project "%ROOT%src\Mux.Desktop\Mux.Desktop.csproj" --framework %TFM% -p:TargetFrameworks=%TFM%
endlocal & exit /b %errorlevel%

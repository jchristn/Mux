@echo off
REM Register the mux agent (the system-tray host that runs the local REST server) to start at login
REM for the current user, via the HKCU Run registry key, and start it now. No administrator rights
REM needed. Builds and installs (or refreshes) the per-user agent in
REM %LOCALAPPDATA%\Programs\Mux\Agent, the same copy install-tool.bat installs (with its Start Menu
REM shortcut), and registers that copy; quit any running agent first. Never touches the all-users copy
REM in %ProgramFiles%. The install is done by mux-apps.ps1.
REM Usage: scripts\windows\run-at-startup.bat [net8.0^|net10.0]
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)
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

tasklist /FI "IMAGENAME eq Mux.Agent.exe" /FI "USERNAME eq %USERDOMAIN%\%USERNAME%" 2>nul | find /I "Mux.Agent.exe" >nul
if not errorlevel 1 (
    echo Error: Another mux agent is running ^(for example from Program Files or a source build^). Quit it ^(or run scripts\windows\kill-mux.bat^) and try again. 1>&2
    exit /b 1
)
if not defined LOCALAPPDATA (
    echo Error: LOCALAPPDATA is not set, so the per-user install folder is unknown. 1>&2
    exit /b 1
)
REM Install or refresh the per-user agent (a self-contained publish).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mux-apps.ps1" -Action InstallUserAgent -Framework %TFM%
if errorlevel 1 exit /b 1
set "AGENT_EXE=%LOCALAPPDATA%\Programs\Mux\Agent\Mux.Agent.exe"
if exist "%AGENT_EXE%" goto have_agent
echo Error: Mux.Agent.exe was not found at "%AGENT_EXE%" after the install. 1>&2
exit /b 1
:have_agent

REM The agent registers itself at login on its first run unless this marker exists. Login startup is
REM managed by these scripts, so write the marker.
set "MUX_CONFIG_HOME=%MUX_CONFIG_DIR%"
if not defined MUX_CONFIG_HOME set "MUX_CONFIG_HOME=%USERPROFILE%\.mux"
if not exist "%MUX_CONFIG_HOME%" mkdir "%MUX_CONFIG_HOME%"
if not exist "%MUX_CONFIG_HOME%\startup-registered" echo registered by scripts\windows> "%MUX_CONFIG_HOME%\startup-registered"

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MuxAgent /t REG_SZ /d "\"%AGENT_EXE%\"" /f >nul
if errorlevel 1 (
    echo Error: Failed to write the Run registry key. 1>&2
    exit /b 1
)
echo Registered "%AGENT_EXE%" to run at login.

start "" "%AGENT_EXE%"
echo Started the agent now; look for the mux icon in the system tray. It runs the REST server.
endlocal
exit /b 0

@echo off
REM Deregister the mux agent from starting at login (deletes the MuxAgent value from the current
REM user's Run registry key). A running agent keeps running; exit it from the tray menu or run
REM scripts\windows\kill-mux.bat. The installed agent (%LOCALAPPDATA%\Programs\Mux\Agent) stays in
REM place; remove-tool.bat removes it.
REM Usage: scripts\windows\remove-from-startup.bat
setlocal
if not "%~1"=="" (
    echo Error: Unexpected argument: %1 ^(this script takes no arguments^) 1>&2
    exit /b 1
)
REM The agent registers itself at login on its first run unless this marker exists. Login startup is
REM managed by these scripts, so write the marker: the agent must not re-register itself after it was removed.
set "MUX_CONFIG_HOME=%MUX_CONFIG_DIR%"
if not defined MUX_CONFIG_HOME set "MUX_CONFIG_HOME=%USERPROFILE%\.mux"
if not exist "%MUX_CONFIG_HOME%" mkdir "%MUX_CONFIG_HOME%"
if not exist "%MUX_CONFIG_HOME%\startup-registered" echo registered by scripts\windows> "%MUX_CONFIG_HOME%\startup-registered"

reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MuxAgent >nul 2>&1
if errorlevel 1 (
    echo The agent is not registered to run at login; nothing to do.
    exit /b 0
)
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MuxAgent /f >nul
if errorlevel 1 (
    echo Error: Failed to delete the Run registry value. 1>&2
    exit /b 1
)
echo Removed the agent from startup.
echo If an agent is still running, exit it from the tray menu or run scripts\windows\kill-mux.bat.
endlocal
exit /b 0

@echo off
setlocal
REM Deregister the mux agent from starting at login (deletes the MuxAgent value
REM from the current user's Run registry key). A currently running agent is left
REM alone; stop it from the tray menu (Exit) if you want it gone now.

reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MuxAgent >nul 2>&1
if errorlevel 1 (
    echo The agent is not registered to run at login; nothing to do.
    exit /b 0
)

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MuxAgent /f >nul
if errorlevel 1 (
    echo Failed to delete the Run registry value.
    exit /b 1
)
echo Removed the agent from startup. A running agent keeps running until you
echo exit it from the tray menu.
endlocal

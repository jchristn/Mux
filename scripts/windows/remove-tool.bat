@echo off
REM Uninstall the mux global tool (Mux.Cli). Succeeds with a message when mux is not installed.
REM Also removes the desktop app (Mux) and the tray agent (Mux Agent) and their Start Menu shortcuts:
REM the per-user copy in %LOCALAPPDATA%\Programs\Mux and, from an elevated prompt, the all-users copy in
REM %ProgramFiles%\Mux (without elevation a warning names what is left; MUX_SYSTEM_INSTALL=0 leaves it).
REM The work is done by mux-apps.ps1.
REM Login registration (run-at-startup.bat) is separate; remove it with remove-from-startup.bat.
REM Usage: scripts\windows\remove-tool.bat
setlocal
if not "%~1"=="" (
    echo Error: Unexpected argument: %1 ^(this script takes no arguments^) 1>&2
    exit /b 1
)
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)

echo mux tool removal (Windows)
echo [1/3] Checking for running mux processes...
tasklist /FI "IMAGENAME eq mux.exe" /FI "USERNAME eq %USERDOMAIN%\%USERNAME%" 2>nul | find /I "mux.exe" >nul
if not errorlevel 1 (
    echo Error: A running mux process is using the global tool. Exit all mux sessions ^(or run scripts\windows\kill-mux.bat^) and try again. 1>&2
    exit /b 1
)
REM Check the apps now too, so a running app cannot leave a half-done removal.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mux-apps.ps1" -Action Check
if errorlevel 1 exit /b 1
set "MUX_INSTALLED=0"
dotnet tool list -g 2>nul | findstr /R /I /B /C:"mux\.cli " >nul
if not errorlevel 1 set "MUX_INSTALLED=1"
echo [2/3] Removing the mux global tool...
if "%MUX_INSTALLED%"=="1" (
    dotnet tool uninstall -g Mux.Cli
    if errorlevel 1 (
        echo Error: Failed to uninstall mux. 1>&2
        exit /b 1
    )
) else (
    echo mux is not installed; nothing to remove.
)
echo [3/3] Removing the desktop app and tray agent...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mux-apps.ps1" -Action Remove
if errorlevel 1 exit /b 1
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MuxAgent >nul 2>&1
if not errorlevel 1 echo Note: the agent is still registered to start at login. Run scripts\windows\remove-from-startup.bat to remove that.
echo Done.
endlocal
exit /b 0

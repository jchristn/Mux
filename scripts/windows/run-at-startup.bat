@echo off
setlocal
REM Register the mux agent (the system-tray host that runs the local REST server)
REM to start at login for the current user, via the HKCU Run registry key. No
REM administrator rights needed. Resolves the agent exe from dist\ (published
REM layout) first, then the dev build output.

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"

set "AGENT_EXE=%REPO_ROOT%\dist\Mux.Agent.exe"
if not exist "%AGENT_EXE%" set "AGENT_EXE=%REPO_ROOT%\src\Mux.Agent\bin\Release\net10.0\Mux.Agent.exe"
if not exist "%AGENT_EXE%" set "AGENT_EXE=%REPO_ROOT%\src\Mux.Agent\bin\Release\net8.0\Mux.Agent.exe"
if not exist "%AGENT_EXE%" set "AGENT_EXE=%REPO_ROOT%\src\Mux.Agent\bin\Debug\net10.0\Mux.Agent.exe"

if not exist "%AGENT_EXE%" (
    echo Mux.Agent.exe not found. Build it first, for example:
    echo   dotnet build src\Mux.Agent\Mux.Agent.csproj -c Release
    exit /b 1
)

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MuxAgent /t REG_SZ /d "\"%AGENT_EXE%\"" /f >nul
if errorlevel 1 (
    echo Failed to write the Run registry key.
    exit /b 1
)
echo Registered "%AGENT_EXE%" to run at login.

tasklist /FI "IMAGENAME eq Mux.Agent.exe" | find /I "Mux.Agent.exe" >nul
if errorlevel 1 (
    start "" "%AGENT_EXE%"
    echo Started the agent now; look for the mux icon in the system tray. It runs the REST server.
) else (
    echo The agent is already running.
)
endlocal

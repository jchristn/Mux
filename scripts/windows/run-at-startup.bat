@echo off
REM Register the mux agent (the system-tray host that runs the local REST server) to start at login
REM for the current user, via the HKCU Run registry key, and start it now. No administrator rights
REM needed. Builds Mux.Agent (Release) for the selected framework first, so stop a running agent
REM beforehand.
REM Usage: scripts\windows\run-at-startup.bat [net8.0^|net10.0]
setlocal
for %%i in ("%~dp0..\..") do set "REPO_ROOT=%%~fi"
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
    echo Error: The mux agent is running. Stop it ^(scripts\windows\kill-mux.bat^) and try again, so the build can replace it. 1>&2
    exit /b 1
)
echo Building Mux.Agent ^(Release, %TFM%^)...
dotnet build "%REPO_ROOT%\src\Mux.Agent\Mux.Agent.csproj" -c Release -f %TFM% -p:TargetFrameworks=%TFM% -nologo -v quiet
if errorlevel 1 (
    echo Error: Building Mux.Agent failed. 1>&2
    exit /b 1
)
set "AGENT_EXE=%REPO_ROOT%\src\Mux.Agent\bin\Release\%TFM%\Mux.Agent.exe"
if exist "%AGENT_EXE%" goto have_agent
echo Error: Mux.Agent.exe was not found at %AGENT_EXE% after the build. 1>&2
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

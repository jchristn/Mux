@echo off
setlocal
REM Register the mux agent (the system-tray host that runs the local REST server)
REM to start at login for the current user, via the HKCU Run registry key. No
REM administrator rights needed. Resolves the agent exe from dist\ (published
REM layout) first, then the dev build output.

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"

call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Unexpected argument: %1. 1>&2
    exit /b 1
)
set "AGENT_EXE=%REPO_ROOT%\src\Mux.Agent\bin\Release\%TFM%\Mux.Agent.exe"
if not exist "%AGENT_EXE%" set "AGENT_EXE=%REPO_ROOT%\src\Mux.Agent\bin\Debug\%TFM%\Mux.Agent.exe"
if "%FRAMEWORK_ARGS%"=="0" if not defined MUX_TFM if exist "%REPO_ROOT%\dist\Mux.Agent.exe" set "AGENT_EXE=%REPO_ROOT%\dist\Mux.Agent.exe"

if not exist "%AGENT_EXE%" (
    echo Mux.Agent.exe not found. Build it first, for example:
    echo   dotnet build src\Mux.Agent\Mux.Agent.csproj -c Release -f %TFM% -p:TargetFrameworks=%TFM%
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

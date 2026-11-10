@echo off
REM Build and run the mux terminal UI (TUI) from source (bin output) instead of the installed
REM global 'mux' tool. IMPORTANT: 'dotnet build' does NOT update the global 'mux' tool -- that
REM requires reinstall-tool.bat. Use this script to test terminal changes against the current
REM build without reinstalling, so the TUI always matches the freshly built desktop/agent/web.
REM The TUI runs against the directory you launch this script from (its working directory).
REM Usage: scripts\windows\run-tui.bat [net8.0^|net10.0]
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
dotnet run --project "%ROOT%src\Mux.Cli\Mux.Cli.csproj" --framework %TFM% -p:TargetFrameworks=%TFM%
endlocal & exit /b %errorlevel%

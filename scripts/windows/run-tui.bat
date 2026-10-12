@echo off
REM Build and run the mux terminal UI (TUI) from source (bin output) instead of the installed
REM global 'mux' tool. IMPORTANT: 'dotnet build' does NOT update the global 'mux' tool -- that
REM requires reinstall-tool.bat. Use this script to test terminal changes against the current
REM build without reinstalling, so the TUI always matches the freshly built desktop/agent/web.
REM The TUI runs against the directory you launch this script from (its working directory).
REM Usage: scripts\windows\run-tui.bat [net8.0^|net10.0]
setlocal
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
chcp 65001 >nul
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Unexpected argument: %1. 1>&2
    exit /b 1
)
dotnet run --project "%ROOT%src\Mux.Cli\Mux.Cli.csproj" --framework %TFM% -p:TargetFrameworks=%TFM%
endlocal

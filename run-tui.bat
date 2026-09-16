@echo off
REM Build and run the mux terminal UI (TUI) from source (bin output) instead of the installed
REM global 'mux' tool. IMPORTANT: 'dotnet build' does NOT update the global 'mux' tool — that
REM requires reinstall-tool.bat. Use this script to test terminal changes against the current
REM build without reinstalling, so the TUI always matches the freshly built desktop/agent/web.
REM The TUI runs against the directory you launch this script from (its working directory).
REM Usage: run-tui.bat [net8.0^|net10.0]
setlocal
chcp 65001 >nul
set TFM=%1
if "%TFM%"=="" set TFM=net10.0
dotnet run --project "%~dp0src\Mux.Cli\Mux.Cli.csproj" --framework %TFM%
endlocal

@echo off
REM Launch the mux system-tray agent (hosts the local REST server in the background).
REM Usage: scripts\windows\run-agent.bat [net8.0^|net10.0]
setlocal
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
set TFM=%1
if "%TFM%"=="" set TFM=net10.0
dotnet run --project "%ROOT%src\Mux.Agent\Mux.Agent.csproj" --framework %TFM%
endlocal

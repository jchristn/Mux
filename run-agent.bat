@echo off
REM Launch the mux system-tray agent (hosts the local REST server in the background).
REM Usage: run-agent.bat [net8.0^|net10.0]
setlocal
set TFM=%1
if "%TFM%"=="" set TFM=net10.0
dotnet run --project "%~dp0src\Mux.Agent\Mux.Agent.csproj" --framework %TFM%
endlocal

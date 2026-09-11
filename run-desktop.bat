@echo off
REM Launch the mux desktop application (Avalonia).
REM Usage: run-desktop.bat [net8.0^|net10.0]
setlocal
set TFM=%1
if "%TFM%"=="" set TFM=net10.0
dotnet run --project "%~dp0src\Mux.Desktop\Mux.Desktop.csproj" --framework %TFM%
endlocal

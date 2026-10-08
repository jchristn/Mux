@echo off
REM Launch the mux desktop application (Avalonia).
REM Usage: scripts\windows\run-desktop.bat [net8.0^|net10.0]
setlocal
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
chcp 65001 >nul
set TFM=%1
if "%TFM%"=="" set TFM=net10.0
dotnet run --project "%ROOT%src\Mux.Desktop\Mux.Desktop.csproj" --framework %TFM%
endlocal

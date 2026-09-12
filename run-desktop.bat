@echo off
REM Launch the mux desktop application (Avalonia).
REM Usage: run-desktop.bat [net8.0^|net10.0]
REM Classic logo, kept for easy revert:
REM    _____ _ _ _ _
REM   ^|     ^| ^| ^|_'_^|
REM   ^|_^|_^|_^|___^|_,_^|
setlocal
chcp 65001 >nul
echo.
echo ▄▄   ▄▄ ▄▄ ▄▄ ▄▄ ▄▄
echo ██▀▄▀██ ██ ██ ▀█▄█▀
echo ██   ██ ▀███▀ ██ ██
echo.
echo  mux desktop
echo.
set TFM=%1
if "%TFM%"=="" set TFM=net10.0
dotnet run --project "%~dp0src\Mux.Desktop\Mux.Desktop.csproj" --framework %TFM%
endlocal

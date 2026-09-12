@echo off
REM Launch the mux desktop application (Avalonia).
REM Usage: run-desktop.bat [net8.0^|net10.0]
setlocal
echo.
echo  _____ _ _ _ _
echo ^|     ^| ^| ^|_'_^|
echo ^|_^|_^|_^|___^|_,_^|
echo.
echo  mux desktop
echo.
set TFM=%1
if "%TFM%"=="" set TFM=net10.0
dotnet run --project "%~dp0src\Mux.Desktop\Mux.Desktop.csproj" --framework %TFM%
endlocal

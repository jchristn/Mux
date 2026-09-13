@echo off
REM Publish a self-contained, single-file mux Desktop build for one runtime.
REM
REM Usage:
REM   publish-desktop.bat [RID] [TFM]
REM
REM RID  defaults to win-x64. Pass win-arm64, linux-x64, osx-arm64, etc. to cross-publish.
REM TFM  defaults to net10.0 (pass net8.0 for the LTS build).
REM
REM Output lands in dist\desktop\<RID>\. The build is self-contained: the target
REM machine does NOT need the .NET runtime installed. Artifacts are UNSIGNED - see
REM docs\DESKTOP.md ("Packaging") for the code-signing / notarization steps, which
REM require your platform signing certificates.
setlocal
chcp 65001 >nul

set RID=%1
if "%RID%"=="" set RID=win-x64
set TFM=%2
if "%TFM%"=="" set TFM=net10.0
set OUT=%~dp0dist\desktop\%RID%

echo Publishing mux Desktop
echo   project: %~dp0src\Mux.Desktop\Mux.Desktop.csproj
echo   runtime: %RID%
echo   target:  %TFM%
echo   output:  %OUT%
echo.

dotnet publish "%~dp0src\Mux.Desktop\Mux.Desktop.csproj" ^
  -c Release ^
  -f %TFM% ^
  -r %RID% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none ^
  -o "%OUT%"

if errorlevel 1 (
  echo.
  echo Publish FAILED.
  endlocal
  exit /b 1
)

echo.
echo Done. Self-contained (unsigned) build in: %OUT%
echo Distribute the whole folder; the launcher is "Mux.Desktop.exe".
endlocal

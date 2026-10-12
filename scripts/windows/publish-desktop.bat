@echo off
REM Publish a self-contained, single-file mux Desktop build for one runtime.
REM
REM Usage:
REM   scripts\windows\publish-desktop.bat [RID] [TFM]
REM
REM RID  defaults to win-x64. Pass win-arm64, linux-x64, osx-arm64, etc. to cross-publish.
REM TFM  defaults to MUX_TFM, then net10.0 with a .NET 10 SDK, otherwise net8.0.
REM
REM Output lands in dist\desktop\<RID>\. The build is self-contained: the target
REM machine does NOT need the .NET runtime installed. Artifacts are UNSIGNED - see
REM docs\DESKTOP.md ("Packaging") for the code-signing / notarization steps, which
REM require your platform signing certificates.
setlocal
chcp 65001 >nul
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"

set "RID=win-x64"
set "FIRST=%~1"
if defined FIRST if /I not "%FIRST:~0,3%"=="net" if not "%FIRST:~0,1%"=="-" (
  set "RID=%~1"
  shift
)
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Unexpected argument: %1. 1>&2
    exit /b 1
)
set OUT=%ROOT%dist\desktop\%RID%

echo Publishing mux Desktop
echo   project: %ROOT%src\Mux.Desktop\Mux.Desktop.csproj
echo   runtime: %RID%
echo   target:  %TFM%
echo   output:  %OUT%
echo.

dotnet publish "%ROOT%src\Mux.Desktop\Mux.Desktop.csproj" ^
  -c Release ^
  -f %TFM% ^
  -p:TargetFrameworks=%TFM% ^
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

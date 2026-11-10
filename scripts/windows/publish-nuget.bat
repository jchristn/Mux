@echo off
setlocal enabledelayedexpansion
rem publish-nuget.bat - pack and publish the mux NuGet libraries (and their .snupkg symbol
rem packages) to nuget.org.
rem
rem Usage:  scripts\windows\publish-nuget.bat ^<nuget-api-key^> [net8.0^|net10.0]
rem
rem Each project is packed fresh in Release so the current version in its .csproj is what
rem gets published. Pushing a .nupkg also uploads the matching .snupkg symbol package.

if /i "%~1"=="-h" goto usage
if /i "%~1"=="--help" goto usage
if /i "%~1"=="/?" goto usage
set "FIRST=%~1"
if not defined FIRST goto usage_error
if "%FIRST:~0,1%"=="-" goto usage_error
if /i "%FIRST:~0,3%"=="net" goto usage_error

set "APIKEY=%~1"
shift
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)
REM "call x.bat /?" shows CALL's own help, so handle /? here.
if "%~1"=="/?" (
    call "%~dp0resolve-framework.bat" --help
    exit /b 0
)
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Error: Unexpected argument: %1 1>&2
    exit /b 1
)
set "SOURCE=https://api.nuget.org/v3/index.json"
set "CONFIG=Release"
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
set "OUTDIR=%ROOT%artifacts\nuget"

rem Publish in dependency order (a package's dependencies should exist first).
set "PROJECTS=Mux.Search Mux.Core Mux.Server Mux.Desktop.Core"

echo.
echo Cleaning %OUTDIR%
if exist "%OUTDIR%" rmdir /s /q "%OUTDIR%"
mkdir "%OUTDIR%"

REM Packages ship every target framework the libraries build (src\Directory.Build.props). An explicit
REM framework (argument or MUX_TFM) packs only that one; nuget.org versions cannot be replaced later.
set "FRAMEWORK_PROP="
if not "%FRAMEWORK_ARGS%"=="0" set "FRAMEWORK_PROP=-p:TargetFrameworks=%TFM%"
if defined MUX_TFM set "FRAMEWORK_PROP=-p:TargetFrameworks=%TFM%"
if defined FRAMEWORK_PROP echo Packing for %TFM% only ^(explicit framework^).

echo.
echo === Packing ===
for %%P in (%PROJECTS%) do (
  echo Packing %%P ...
  dotnet pack "%ROOT%src\%%P\%%P.csproj" -c %CONFIG% %FRAMEWORK_PROP% -o "%OUTDIR%" --nologo
  if errorlevel 1 (
    echo Error: pack failed for %%P 1>&2
    exit /b 1
  )
)

echo.
echo === Publishing ===
set "PUSHED=0"
for %%F in ("%OUTDIR%\*.nupkg") do (
  echo Pushing %%~nxF ...
  dotnet nuget push "%%F" --api-key "%APIKEY%" --source "%SOURCE%" --skip-duplicate
  if errorlevel 1 (
    echo Error: push failed for %%~nxF 1>&2
    exit /b 1
  )
  set /a PUSHED+=1
)

if "%PUSHED%"=="0" (
  echo Error: no .nupkg files were produced in %OUTDIR% 1>&2
  exit /b 1
)

echo.
echo Done. Published %PUSHED% package(s) and their symbol files to %SOURCE%.
endlocal
exit /b 0

:usage_error
call :usage 1>&2
exit /b 1

:usage
echo Usage: scripts\windows\publish-nuget.bat ^<nuget-api-key^> [net8.0^|net10.0]
exit /b 0

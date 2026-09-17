@echo off
setlocal enabledelayedexpansion

rem publish-nuget.bat - pack and publish the mux NuGet libraries (and their .snupkg symbol
rem packages) to nuget.org.
rem
rem Usage:  publish-nuget.bat ^<nuget-api-key^>
rem
rem Each project is packed fresh in Release so the current version in its .csproj is what
rem gets published. Pushing a .nupkg also uploads the matching .snupkg symbol package.

if "%~1"=="" (
  echo Usage: publish-nuget.bat ^<nuget-api-key^>
  exit /b 1
)

set "APIKEY=%~1"
set "SOURCE=https://api.nuget.org/v3/index.json"
set "CONFIG=Release"
set "ROOT=%~dp0"
set "OUTDIR=%ROOT%artifacts\nuget"

rem Publish in dependency order (a package's dependencies should exist first).
set "PROJECTS=Mux.Search Mux.Core Mux.Server Mux.Desktop.Core"

echo.
echo Cleaning %OUTDIR%
if exist "%OUTDIR%" rmdir /s /q "%OUTDIR%"
mkdir "%OUTDIR%"

echo.
echo === Packing ===
for %%P in (%PROJECTS%) do (
  echo Packing %%P ...
  dotnet pack "%ROOT%src\%%P\%%P.csproj" -c %CONFIG% -o "%OUTDIR%" --nologo
  if errorlevel 1 (
    echo ERROR: pack failed for %%P
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
    echo ERROR: push failed for %%~nxF
    exit /b 1
  )
  set /a PUSHED+=1
)

if "%PUSHED%"=="0" (
  echo ERROR: no .nupkg files were produced in %OUTDIR%
  exit /b 1
)

echo.
echo Done. Published %PUSHED% package(s) and their symbol files to %SOURCE%.
endlocal
exit /b 0

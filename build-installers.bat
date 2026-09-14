@echo off
setlocal enabledelayedexpansion
REM Build mux Windows installers and drop them under installers\<version>\. Native installer
REM formats are OS-locked, so this builds the Windows channels only (Inno .exe, winget, choco,
REM scoop). Run build-installers.sh on macOS and Linux for their installers, or push a version
REM tag to build all three via GitHub Actions (.github\workflows\release.yml).
REM
REM Usage:
REM   build-installers.bat [VERSION] [--dry-run]
REM
REM VERSION   defaults to the ^<Version^> in src\Mux.Cli\Mux.Cli.csproj.
REM --dry-run prints the plan without building or needing the packagers.

set "REPO_ROOT=%~dp0"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT:~0,-1%"

REM --- Parse arguments -------------------------------------------------------
set "VERSION="
set "DRYRUN="
:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--dry-run" (
  set "DRYRUN=--dry-run"
) else if /i "%~1"=="-h" (
  goto usage
) else if /i "%~1"=="--help" (
  goto usage
) else (
  set "VERSION=%~1"
)
shift
goto parse
:parsed

REM --- Resolve the version ---------------------------------------------------
if not defined VERSION (
  for /f "tokens=3 delims=<>" %%a in ('findstr /c:"<Version>" "%REPO_ROOT%\src\Mux.Cli\Mux.Cli.csproj"') do (
    if not defined VERSION set "VERSION=%%a"
  )
)
if not defined VERSION (
  echo error: could not determine version. Pass it explicitly: build-installers.bat 0.10.0 1>&2
  exit /b 1
)

set "OUT=%REPO_ROOT%\installers\%VERSION%"
set "STAGING=%REPO_ROOT%\dist\staging"
set "PUBLISHER=%REPO_ROOT%\src\Mux.Publisher\Mux.Publisher.csproj"
set "PUB_DLL=%REPO_ROOT%\src\Mux.Publisher\bin\Release\net10.0\Mux.Publisher.dll"

echo mux installers
echo   version : %VERSION%
echo   host os : windows
echo   output  : %OUT%
if defined DRYRUN echo   mode    : DRY RUN (no artifacts produced)
echo.

if not exist "%OUT%" mkdir "%OUT%"

REM --- Build the orchestrator once -------------------------------------------
echo Building Mux.Publisher...
dotnet build "%PUBLISHER%" -c Release -f net10.0 -v quiet -nologo
if errorlevel 1 (
  echo error: failed to build Mux.Publisher. 1>&2
  exit /b 1
)

REM --- Enumerate and run the Windows channels, in dependency order -----------
set "CHANNELS="
for /f "usebackq delims=" %%c in (`dotnet "%PUB_DLL%" --channels-for windows --repo "%REPO_ROOT%"`) do set "CHANNELS=%%c"
if not defined CHANNELS (
  echo No enabled Windows channels in publisher.json.
  exit /b 0
)
echo Channels for windows: %CHANNELS%
echo.

set "FAILED="
for %%x in (%CHANNELS%) do (
  echo ==^> %%x
  dotnet "%PUB_DLL%" --channel %%x --version %VERSION% --repo "%REPO_ROOT%" --out "%OUT%" --staging "%STAGING%" %DRYRUN%
  if errorlevel 1 (
    echo     ^(channel %%x failed - is its packaging tool installed? Inno Setup provides iscc.^)
    set "FAILED=!FAILED! %%x"
  )
  echo.
)

REM --- Summary ---------------------------------------------------------------
echo -------------------------------------------------------------------
if defined DRYRUN (
  echo Dry run complete for windows ^(nothing built^).
) else (
  echo Done. Windows installers are under: %OUT%
  dir /s /b "%OUT%" 2>nul | findstr /v /i "\\publish\\"
)
if defined FAILED (
  echo.
  echo Channels that failed:%FAILED%
  echo Install the missing packagers ^(Inno Setup for iscc^) and re-run, or use --dry-run to preview.
)
echo.
echo Note: this built only Windows installers. Run build-installers.sh on macOS and Linux for
echo their installers - or push a "v%VERSION%" tag to build all three via GitHub Actions.
if defined FAILED exit /b 1
exit /b 0

:usage
echo Usage: build-installers.bat [VERSION] [--dry-run]
echo   VERSION   defaults to the ^<Version^> in src\Mux.Cli\Mux.Cli.csproj
echo   --dry-run prints the plan without building
exit /b 0

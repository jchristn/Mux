@echo off
REM Uninstall, rebuild and reinstall the mux global tool (Mux.Cli) from this checkout.
REM The package is built under artifacts\tool-packages\<framework>.
REM Usage: scripts\windows\reinstall-tool.bat [net8.0^|net10.0]  (or --framework ^<tfm^>; MUX_TFM is also honored)
setlocal
for %%i in ("%~dp0..\..") do set "ROOT_DIR=%%~fi\"
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
set "FRAMEWORK=%TFM%"
set "PACKAGE_SOURCE=%ROOT_DIR%artifacts\tool-packages\%FRAMEWORK%"

echo mux tool reinstall (Windows, %FRAMEWORK%)
echo [1/5] Checking for running mux processes...
tasklist /FI "IMAGENAME eq mux.exe" /FI "USERNAME eq %USERDOMAIN%\%USERNAME%" 2>nul | find /I "mux.exe" >nul
if not errorlevel 1 (
    echo Error: A running mux process is using the global tool. Exit all mux sessions ^(or run scripts\windows\kill-mux.bat^) and try again. 1>&2
    exit /b 1
)
set "MUX_INSTALLED=0"
dotnet tool list -g 2>nul | findstr /R /I /B /C:"mux\.cli " >nul
if not errorlevel 1 set "MUX_INSTALLED=1"
echo [2/5] Removing the existing mux global tool...
if "%MUX_INSTALLED%"=="1" (
    dotnet tool uninstall -g Mux.Cli
    if errorlevel 1 (
        echo Error: Failed to uninstall mux. Ensure no mux processes are running and try again. 1>&2
        exit /b 1
    )
) else (
    echo mux is not installed; continuing.
)
echo [3/5] Building the mux package...
if exist "%PACKAGE_SOURCE%" rmdir /s /q "%PACKAGE_SOURCE%"
mkdir "%PACKAGE_SOURCE%"
if errorlevel 1 exit /b 1
dotnet pack "%ROOT_DIR%src\Mux.Cli\Mux.Cli.csproj" --configuration Release -p:TargetFrameworks=%FRAMEWORK% --output "%PACKAGE_SOURCE%"
if errorlevel 1 (
    echo Error: Build failed. 1>&2
    exit /b 1
)
echo [4/5] Installing the mux global tool...
dotnet tool install -g --source "%PACKAGE_SOURCE%" --framework %FRAMEWORK% --disable-parallel Mux.Cli
if errorlevel 1 (
    echo Error: dotnet tool install failed. 1>&2
    exit /b 1
)
echo [5/5] Verifying the mux command...
REM dotnet tool install puts mux in %USERPROFILE%\.dotnet\tools, which may not be on this
REM session's PATH yet (a new terminal picks it up).
where mux >nul 2>nul || set "PATH=%USERPROFILE%\.dotnet\tools;%PATH%"
mux -v
if errorlevel 1 (
    echo Error: mux was installed, but "mux -v" failed. 1>&2
    exit /b 1
)
echo Done.
endlocal
exit /b 0

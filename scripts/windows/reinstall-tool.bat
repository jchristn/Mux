@echo off
setlocal

for %%i in ("%~dp0..\..") do set "ROOT_DIR=%%~fi\"
set "PACKAGE_ROOT=%ROOT_DIR%artifacts\tool-packages"
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Unexpected argument: %1. 1>&2
    exit /b 1
)
set "FRAMEWORK=%TFM%"

set "PACKAGE_SOURCE=%PACKAGE_ROOT%\%FRAMEWORK%"

echo mux tool reinstall
echo Framework: %FRAMEWORK%
echo Package source: %PACKAGE_SOURCE%
echo.
echo [1/6] Checking for running mux.exe processes...
tasklist /FI "IMAGENAME eq mux.exe" 2>nul | find /I "mux.exe" >nul
if %errorlevel% equ 0 (
    echo A running mux.exe process is locking the global tool install.
    echo Exit all mux sessions and rerun reinstall-tool.bat.
    exit /b 1
)
echo No running mux.exe process found.
echo.

echo [2/6] Removing existing mux global tool if present...
set "UNINSTALL_LOG=%TEMP%\mux-tool-uninstall-%RANDOM%%RANDOM%.log"
dotnet tool uninstall -g Mux.Cli > "%UNINSTALL_LOG%" 2>&1
if errorlevel 1 (
    findstr /I /C:"could not be found" /C:"not currently installed" "%UNINSTALL_LOG%" >nul
    if errorlevel 1 (
        type "%UNINSTALL_LOG%"
        del "%UNINSTALL_LOG%" >nul 2>nul
        echo Failed to uninstall mux. Ensure no mux processes are running and rerun this script.
        exit /b 1
    )
    echo mux is not installed; continuing...
) else (
    type "%UNINSTALL_LOG%"
)
del "%UNINSTALL_LOG%" >nul 2>nul
echo.

echo [3/6] Preparing package directory...
if exist "%PACKAGE_SOURCE%" rmdir /s /q "%PACKAGE_SOURCE%"
mkdir "%PACKAGE_SOURCE%"
if %errorlevel% neq 0 exit /b %errorlevel%
echo Package directory ready.
echo.

echo [4/6] Building mux package for %FRAMEWORK%...
echo Running: dotnet pack "%ROOT_DIR%src\Mux.Cli\Mux.Cli.csproj" --configuration Release -p:TargetFrameworks=%FRAMEWORK% --output "%PACKAGE_SOURCE%"
dotnet pack "%ROOT_DIR%src\Mux.Cli\Mux.Cli.csproj" --configuration Release -p:TargetFrameworks=%FRAMEWORK% --output "%PACKAGE_SOURCE%"
if %errorlevel% neq 0 (
    echo Build failed.
    exit /b %errorlevel%
)
echo Build complete.
echo.

echo [5/6] Installing mux global tool...
echo Running: dotnet tool install -g --source "%PACKAGE_SOURCE%" --framework %FRAMEWORK% --disable-parallel Mux.Cli
dotnet tool install -g --source "%PACKAGE_SOURCE%" --framework %FRAMEWORK% --disable-parallel Mux.Cli
if %errorlevel% neq 0 exit /b %errorlevel%
echo Install complete.
echo.

echo [6/6] Verifying mux command...
mux -v
exit /b %errorlevel%

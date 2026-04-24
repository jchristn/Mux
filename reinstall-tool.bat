@echo off
tasklist /FI "IMAGENAME eq mux.exe" | find /I "mux.exe" >nul
if %errorlevel% equ 0 (
    echo A running mux.exe process is locking the global tool install.
    echo Exit all mux sessions and rerun reinstall-tool.bat.
    exit /b 1
)

echo Removing mux...
dotnet tool uninstall -g Mux.Cli
if %errorlevel% neq 0 (
    echo Failed to uninstall mux. Ensure no mux processes are running and rerun this script.
    exit /b %errorlevel%
)

echo Building mux...
dotnet pack src\Mux.Cli\Mux.Cli.csproj --configuration Release
if %errorlevel% neq 0 (
    echo Build failed.
    exit /b %errorlevel%
)
echo Installing mux...
dotnet tool install -g --add-source src\Mux.Cli\bin\Release Mux.Cli
if %errorlevel% neq 0 exit /b %errorlevel%
mux -v

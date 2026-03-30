@echo off
echo Removing mux...
dotnet tool uninstall -g Mux.Cli 2>nul
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

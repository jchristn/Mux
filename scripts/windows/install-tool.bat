@echo off
setlocal

for %%i in ("%~dp0..\..") do set "ROOT_DIR=%%~fi\"
set "PACKAGE_SOURCE=%ROOT_DIR%artifacts\tool-packages"

call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
if not "%~1"=="" (
    echo Unexpected argument: %1. 1>&2
    exit /b 1
)
set "FRAMEWORK=%TFM%"

if exist "%PACKAGE_SOURCE%" rmdir /s /q "%PACKAGE_SOURCE%"
mkdir "%PACKAGE_SOURCE%"
if %errorlevel% neq 0 exit /b %errorlevel%

echo Building mux for %FRAMEWORK%...
dotnet pack "%ROOT_DIR%src\Mux.Cli\Mux.Cli.csproj" --configuration Release -p:TargetFrameworks=%FRAMEWORK% --output "%PACKAGE_SOURCE%"
if %errorlevel% neq 0 (
    echo Build failed.
    exit /b %errorlevel%
)
echo Installing mux...
dotnet tool install -g --source "%PACKAGE_SOURCE%" --framework %FRAMEWORK% --disable-parallel Mux.Cli
if %errorlevel% neq 0 exit /b %errorlevel%
mux -v
exit /b %errorlevel%

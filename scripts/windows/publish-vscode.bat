@echo off
REM Publish the mux VS Code extension (src\Mux.VSCode) to the VS Code Marketplace.
REM
REM Usage:
REM   scripts\windows\publish-vscode.bat <marketplace-token>
REM   scripts\windows\publish-vscode.bat     (prompts for the token)
REM
REM The token is a VS Code Marketplace Personal Access Token, created in Azure
REM DevOps with scope "Marketplace: Manage" and organization "All accessible
REM organizations". See PUBLISH_VSCODE.md for how to create it and how to create
REM the "usemux" publisher.
REM
REM Publishes whatever version is in src\Mux.VSCode\package.json. To re-publish,
REM bump "version" there first - the Marketplace rejects a version that already
REM exists. Compilation runs automatically via the extension's vscode:prepublish
REM script, so no separate build step is needed here.
setlocal
chcp 65001 >nul

if /i "%~1"=="-h" goto usage
if /i "%~1"=="--help" goto usage
if /i "%~1"=="/?" goto usage
set "FIRST=%~1"
if defined FIRST if "%FIRST:~0,1%"=="-" (
  echo Error: Unknown option: %1 1>&2
  exit /b 1
)
if not "%~2"=="" (
  echo Error: Unexpected argument: %2 1>&2
  exit /b 1
)
set "TOKEN=%~1"
REM Read the token without echoing it.
if not defined TOKEN for /f "usebackq delims=" %%T in (`powershell -NoProfile -Command "$s = Read-Host 'Enter your VS Code Marketplace token' -AsSecureString; [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))"`) do set "TOKEN=%%T"
if "%TOKEN%"=="" (
  echo. 1>&2
  echo Error: No token provided. Aborting. 1>&2
  endlocal
  exit /b 1
)

where npm >nul 2>nul
if errorlevel 1 (
  echo Error: npm was not found on PATH. Install Node.js. 1>&2
  exit /b 1
)
where npx >nul 2>nul
if errorlevel 1 (
  echo Error: npx was not found on PATH. Install Node.js. 1>&2
  exit /b 1
)

for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
set "EXTDIR=%ROOT%src\Mux.VSCode"

echo Publishing the mux VS Code extension
echo   project: %EXTDIR%
echo.

pushd "%EXTDIR%"

echo Installing dependencies...
call npm install
if errorlevel 1 (
  echo.
  echo npm install FAILED. Is Node.js installed and on PATH?
  popd
  endlocal
  exit /b 1
)

echo.
echo Publishing to the VS Code Marketplace...
call npx --yes @vscode/vsce publish -p "%TOKEN%"
if errorlevel 1 (
  echo.
  echo Publish FAILED. See the error above and PUBLISH_VSCODE.md for troubleshooting
  echo   401                       - token scope/org wrong, or publisher owned by another account
  echo   name/displayName taken    - bump or change name/displayName in package.json
  echo   version already exists    - bump "version" in src\Mux.VSCode\package.json
  popd
  endlocal
  exit /b 1
)

popd
echo.
echo Done. Published to the VS Code Marketplace:
echo   https://marketplace.visualstudio.com/items?itemName=usemux.mux-ai
endlocal
exit /b 0

:usage
echo Usage: scripts\windows\publish-vscode.bat [marketplace-token]
exit /b 0

@echo off
REM Publish the mux VS Code extension (src\Mux.VSCode) to the VS Code Marketplace.
REM
REM Usage:
REM   publish-vscode.bat <marketplace-token>
REM   publish-vscode.bat                       (prompts for the token)
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

set "TOKEN=%~1"
if "%TOKEN%"=="" set /p "TOKEN=Enter your VS Code Marketplace token: "
if "%TOKEN%"=="" (
  echo.
  echo No token provided. Aborting.
  endlocal
  exit /b 1
)

set "EXTDIR=%~dp0src\Mux.VSCode"

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

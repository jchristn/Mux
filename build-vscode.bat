@echo off
REM Build the mux VS Code extension (src\Mux.VSCode) into a .vsix package.
REM Usage: build-vscode.bat
REM The .vsix is written to src\Mux.VSCode\ and named from the "name" and "version" in its package.json.
REM Compilation runs automatically through the extension's vscode:prepublish script.
setlocal
chcp 65001 >nul

set "EXTDIR=%~dp0src\Mux.VSCode"

where npm >nul 2>nul
if errorlevel 1 (
  echo npm not found. Install Node.js and try again.
  endlocal
  exit /b 1
)

echo Building the mux VS Code extension
echo   project: %EXTDIR%
echo.

pushd "%EXTDIR%"

echo Installing dependencies...
call npm ci
if errorlevel 1 (
  echo.
  echo npm ci FAILED.
  popd
  endlocal
  exit /b 1
)

echo.
echo Packaging...
call npx --yes @vscode/vsce package
if errorlevel 1 (
  echo.
  echo Packaging FAILED. See the error above.
  popd
  endlocal
  exit /b 1
)

set "VSIX="
for /f "delims=" %%f in ('dir /b /o-d *.vsix') do if not defined VSIX set "VSIX=%%~ff"
popd

echo.
echo Done: %VSIX%
echo Install with: code --install-extension "%VSIX%"
echo   (or in VS Code: Extensions ^> ... ^> Install from VSIX...)
endlocal

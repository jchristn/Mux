@echo off
REM Build every Windows installer (Inno Setup .exe, archives) into installers\<version>\.
REM Usage: build-installers.bat [VERSION] [--framework net8.0 or net10.0] [--dry-run]
REM On macOS and Linux run ./build-installers.sh. The release workflow builds all three OSes on a v* tag.
call "%~dp0scripts\windows\build-installers.bat" %*
exit /b %ERRORLEVEL%

@echo off
REM Regenerate BUILTIN_SKILLS.md from the default skill library and the bundled packs.
REM Usage: scripts\windows\generate-builtin-skills.bat [net8.0^|net10.0] [--framework TFM] [--check] [--no-build]
REM   --check     exit 1 when BUILTIN_SKILLS.md is out of date instead of rewriting it
REM   --no-build  reuse the existing Debug build for the selected framework of Mux.Cli
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)
set "PY="
where python >nul 2>nul && set "PY=python"
where py >nul 2>nul && set "PY=py -3"
if not defined PY (
    echo Error: Python 3 was not found on PATH ^(python or py^). Install Python 3. 1>&2
    exit /b 1
)
%PY% "%~dp0..\common\generate-builtin-skills.py" %*
set CODE=%ERRORLEVEL%
endlocal & exit /b %CODE%

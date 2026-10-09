@echo off
REM Regenerate BUILTIN_SKILLS.md from the default skill library and the bundled packs.
REM Usage: scripts\windows\generate-builtin-skills.bat [--check] [--no-build]
REM   --check     exit 1 when BUILTIN_SKILLS.md is out of date instead of rewriting it
REM   --no-build  reuse the existing Debug net10.0 build of Mux.Cli
setlocal
set "PY=python"
where py >nul 2>nul && set "PY=py -3"
%PY% "%~dp0..\common\generate-builtin-skills.py" %*
set CODE=%ERRORLEVEL%
endlocal & exit /b %CODE%

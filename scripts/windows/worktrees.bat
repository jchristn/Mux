@echo off
REM List, prune, or remove the isolated git worktrees mux keeps for subagents and jobs (mux worktree), using the
REM build from source. Runs against the repository containing the directory you launch it from.
REM Prefix arguments with net8.0 or --framework net8.0; MUX_TFM is also supported.
REM Usage: scripts\windows\worktrees.bat [list ^| prune ^| remove ^<name^> [--force] [--keep-branch]] [--output-format json]
setlocal
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
set "ARGS="
:collect_args
if "%~1"=="" goto args_ready
set "ARGS=%ARGS% %1"
shift
goto collect_args
:args_ready
if "%ARGS%"=="" set ARGS=list
dotnet run --project "%ROOT%src\Mux.Cli\Mux.Cli.csproj" --framework %TFM% -p:TargetFrameworks=%TFM% -- worktree %ARGS% --cwd "%CD%"
endlocal

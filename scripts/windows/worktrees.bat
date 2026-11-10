@echo off
REM List, prune, or remove the isolated git worktrees mux keeps for subagents and jobs (mux worktree), using the
REM build from source. Runs against the repository containing the directory you launch it from.
REM Prefix arguments with net8.0 or --framework net8.0; MUX_TFM is also supported.
REM Usage: scripts\windows\worktrees.bat [list ^| prune ^| remove ^<name^> [--force] [--keep-branch]] [--output-format json]
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
REM "call x.bat /?" shows CALL's own help, so handle /? here.
if "%~1"=="/?" (
    call "%~dp0resolve-framework.bat" --help
    exit /b 0
)
call "%~dp0resolve-framework.bat" %1 %2
if errorlevel 2 exit /b 0
if errorlevel 1 exit /b 1
REM With no framework argument, forward the arguments exactly as given. After shift, the full argument
REM list is not updated, so a positional framework means rebuilding the list, which loses "=", "," and ";" inside
REM values: pass the framework through MUX_TFM instead when your arguments contain them.
set "ARGS=%*"
if "%FRAMEWORK_ARGS%"=="0" goto args_ready
for /l %%i in (1,1,%FRAMEWORK_ARGS%) do shift
set "ARGS="
:collect_args
if "%~1"=="" goto args_ready
set "ARGS=%ARGS% %1"
shift
goto collect_args
:args_ready
if not defined ARGS set "ARGS=list"
REM "%CD%." keeps a drive root (C:\) from escaping the closing quote; "C:\." is the same directory.
dotnet run --project "%ROOT%src\Mux.Cli\Mux.Cli.csproj" --framework %TFM% -p:TargetFrameworks=%TFM% -- worktree %ARGS% --cwd "%CD%."
endlocal & exit /b %errorlevel%

@echo off
REM List, prune, or remove the isolated git worktrees mux keeps for subagents and jobs (mux worktree), using the
REM build from source. Runs against the repository containing the directory you launch it from.
REM Set MUX_TFM to net8.0 or net10.0 (default net10.0).
REM Usage: scripts\windows\worktrees.bat [list ^| prune ^| remove ^<name^> [--force] [--keep-branch]] [--output-format json]
setlocal
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
set TFM=%MUX_TFM%
if "%TFM%"=="" set TFM=net10.0
set ARGS=%*
if "%ARGS%"=="" set ARGS=list
dotnet run --project "%ROOT%src\Mux.Cli\Mux.Cli.csproj" --framework %TFM% -- worktree %ARGS% --cwd "%CD%"
endlocal

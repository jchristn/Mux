@echo off
REM Build mux from source and serve it as an MCP server (mux mcp serve), for MCP clients such as Claude Code,
REM Codex, or mux itself. Remaining arguments are passed to `mux mcp serve`, so the defaults are stdio transport and the
REM deny approval ceiling. The build runs quietly first because stdout is the MCP protocol stream over stdio.
REM Prefix arguments with net8.0 or --framework net8.0; MUX_TFM is also supported.
REM Usage: scripts\windows\run-mcp-server.bat [--http ^<port^>] [--host ^<name^>] [--api-key ^<key^>] [--allow-skills]
REM        [--log-messages] [--approval-policy deny^|auto-safe^|auto] [--endpoint ^<name^>] [-w ^<dir^>]
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
dotnet build "%ROOT%src\Mux.Cli\Mux.Cli.csproj" --framework %TFM% -p:TargetFrameworks=%TFM% -nologo -v quiet 1>&2
if errorlevel 1 exit /b 1
dotnet "%ROOT%src\Mux.Cli\bin\Debug\%TFM%\Mux.Cli.dll" mcp serve %ARGS%
endlocal

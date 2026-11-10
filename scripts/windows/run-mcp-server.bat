@echo off
REM Build mux from source and serve it as an MCP server (mux mcp serve), for MCP clients such as Claude Code,
REM Codex, or mux itself. Remaining arguments are passed to `mux mcp serve`, so the defaults are stdio transport and the
REM deny approval ceiling. The build runs quietly first because stdout is the MCP protocol stream over stdio.
REM Prefix arguments with net8.0 or --framework net8.0; MUX_TFM is also supported.
REM Nothing is written to stdout before the server starts; the build log goes to stderr.
REM Usage: scripts\windows\run-mcp-server.bat [--http ^<port^>] [--host ^<name^>] [--api-key ^<key^>] [--allow-skills]
REM        [--log-messages] [--approval-policy deny^|auto-safe^|auto] [--endpoint ^<name^>] [-w ^<dir^>]
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
set "MUX_DLL=%ROOT%src\Mux.Cli\bin\Debug\%TFM%\Mux.Cli.dll"
dotnet build "%ROOT%src\Mux.Cli\Mux.Cli.csproj" --framework %TFM% -p:TargetFrameworks=%TFM% -nologo -v quiet 1>&2
if not errorlevel 1 goto serve
REM Windows locks the DLL of a server that is already running, so a second client's build fails
REM (MSB3027). Serve the existing build in that case rather than failing; say so on stderr.
if not exist "%MUX_DLL%" exit /b 1
echo Warning: the build failed ^(another mux MCP server may hold the files^); serving the existing build. 1>&2
:serve
dotnet "%MUX_DLL%" mcp serve %ARGS%
endlocal & exit /b %errorlevel%

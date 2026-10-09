@echo off
REM Build mux from source and serve it as an MCP server (mux mcp serve), for MCP clients such as Claude Code,
REM Codex, or mux itself. Every argument is passed to `mux mcp serve`, so the defaults are stdio transport and the
REM deny approval ceiling. The build runs quietly first because stdout is the MCP protocol stream over stdio.
REM Set MUX_TFM to net8.0 or net10.0 (default net10.0).
REM Usage: scripts\windows\run-mcp-server.bat [--http ^<port^>] [--host ^<name^>] [--api-key ^<key^>] [--allow-skills]
REM        [--log-messages] [--approval-policy deny^|auto-safe^|auto] [--endpoint ^<name^>] [-w ^<dir^>]
setlocal
for %%i in ("%~dp0..\..") do set "ROOT=%%~fi\"
set TFM=%MUX_TFM%
if "%TFM%"=="" set TFM=net10.0
dotnet build "%ROOT%src\Mux.Cli\Mux.Cli.csproj" --framework %TFM% -nologo -v quiet 1>&2
if errorlevel 1 exit /b 1
dotnet "%ROOT%src\Mux.Cli\bin\Debug\%TFM%\Mux.Cli.dll" mcp serve %*
endlocal

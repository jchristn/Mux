@echo off
REM Stop every running mux process: the agent, desktop app, CLI/TUI, server, publisher and the
REM installed mux tool in your own session, including copies started with "dotnet <dll>" (for example run-mcp-server.bat).
REM Asks them to exit, waits up to 5 seconds, then force-kills what is left. Succeeds when nothing is
REM running; fails if something could not be stopped.
REM Usage: scripts\windows\kill-mux.bat
setlocal
if not "%~1"=="" (
    echo Error: Unexpected argument: %1 ^(this script takes no arguments^) 1>&2
    exit /b 1
)
echo Stopping mux processes...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$names = 'Mux.Agent','Mux.Desktop','Mux.Cli','Mux.Server','Mux.Publisher','mux';" ^
  "$session = (Get-Process -Id $PID).SessionId;" ^
  "function Find-Mux { Get-CimInstance Win32_Process | Where-Object { $_.SessionId -eq $session } | Where-Object { ($names -contains [IO.Path]::GetFileNameWithoutExtension($_.Name)) -or ($_.Name -ieq 'dotnet.exe' -and $_.CommandLine -match 'Mux\.(Agent|Desktop|Cli|Server|Publisher)\.dll') } };" ^
  "$p = @(Find-Mux); if ($p.Count -eq 0) { Write-Output 'No mux processes are running.'; exit 0 };" ^
  "foreach ($x in $p) { & taskkill.exe /PID $x.ProcessId 2>$null | Out-Null };" ^
  "for ($i = 0; $i -lt 5 -and @(Find-Mux).Count -gt 0; $i++) { Start-Sleep -Seconds 1 };" ^
  "$left = @(Find-Mux); if ($left.Count -gt 0) { Write-Output 'Some mux processes did not exit; force-killing them.'; foreach ($x in $left) { Stop-Process -Id $x.ProcessId -Force -ErrorAction SilentlyContinue }; Start-Sleep -Seconds 1 };" ^
  "$left = @(Find-Mux); if ($left.Count -gt 0) { [Console]::Error.WriteLine('Error: Some mux processes could not be stopped: ' + (($left | ForEach-Object { $_.Name + ' (' + $_.ProcessId + ')' }) -join ', ')); exit 1 };" ^
  "Write-Output 'Done.'"
set "CODE=%errorlevel%"
endlocal & exit /b %CODE%

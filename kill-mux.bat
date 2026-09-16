@echo off
REM Kills every mux-related process. Best-effort: it attempts each one and never stops early if a
REM process isn't running (taskkill's "not found" is ignored so the script continues to the next).

echo Stopping mux processes...

taskkill /F /IM Mux.Agent.exe 2>nul
taskkill /F /IM Mux.Desktop.exe 2>nul
taskkill /F /IM Mux.Cli.exe 2>nul
taskkill /F /IM Mux.Server.exe 2>nul
taskkill /F /IM Mux.Publisher.exe 2>nul
taskkill /F /IM mux.exe 2>nul

echo Done.
exit /b 0

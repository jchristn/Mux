@echo off
rem update.bat - pull the latest pinned images for the mux observability stack and recreate it.
rem Non-destructive: named volumes (Prometheus, Tempo, Loki, and Grafana data) are preserved.
cd /d "%~dp0"
docker compose -f compose.yaml pull
docker compose -f compose.yaml down
docker compose -f compose.yaml up -d
docker ps -a

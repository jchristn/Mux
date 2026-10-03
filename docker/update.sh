#!/usr/bin/env bash
# update.sh - pull the latest pinned images for the mux observability stack and recreate it.
# Non-destructive: named volumes (Prometheus, Tempo, Loki, and Grafana data) are preserved.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
docker compose -f compose.yaml pull
docker compose -f compose.yaml down
docker compose -f compose.yaml up -d
docker ps -a

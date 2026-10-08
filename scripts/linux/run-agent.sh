#!/usr/bin/env bash
# Launch the mux system-tray agent (hosts the local REST server in the background).
# Usage: ./scripts/linux/run-agent.sh [net8.0|net10.0]
set -e
TFM="${1:-net10.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
dotnet run --project "$REPO_ROOT/src/Mux.Agent/Mux.Agent.csproj" --framework "$TFM"

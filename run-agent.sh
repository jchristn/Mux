#!/usr/bin/env bash
# Launch the mux system-tray agent (hosts the local REST server in the background).
# Usage: ./run-agent.sh [net8.0|net10.0]
set -e
TFM="${1:-net10.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet run --project "$SCRIPT_DIR/src/Mux.Agent/Mux.Agent.csproj" --framework "$TFM"

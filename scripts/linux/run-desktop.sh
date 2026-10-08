#!/usr/bin/env bash
# Launch the mux desktop application (Avalonia).
# Usage: ./scripts/linux/run-desktop.sh [net8.0|net10.0]
set -e
TFM="${1:-net10.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
dotnet run --project "$REPO_ROOT/src/Mux.Desktop/Mux.Desktop.csproj" --framework "$TFM"

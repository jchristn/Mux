#!/usr/bin/env bash
# Launch the mux desktop application (Avalonia).
# Usage: ./run-desktop.sh [net8.0|net10.0]
set -e
TFM="${1:-net10.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet run --project "$SCRIPT_DIR/src/Mux.Desktop/Mux.Desktop.csproj" --framework "$TFM"

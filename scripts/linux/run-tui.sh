#!/usr/bin/env bash
# Build and run the mux terminal UI (TUI) from source (bin output) instead of the installed
# global 'mux' tool. IMPORTANT: 'dotnet build' does NOT update the global 'mux' tool — that
# requires reinstall-tool.sh. Use this script to test terminal changes against the current
# build without reinstalling, so the TUI always matches the freshly built desktop/agent/web.
# The TUI runs against the directory you launch this script from (its working directory).
# Usage: ./scripts/linux/run-tui.sh [net8.0|net10.0]
set -e
TFM="${1:-net10.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
dotnet run --project "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM"

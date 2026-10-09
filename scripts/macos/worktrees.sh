#!/usr/bin/env bash
# List, prune, or remove the isolated git worktrees mux keeps for subagents and jobs (mux worktree), using the
# build from source. Runs against the repository containing the directory you launch it from.
# Set MUX_TFM to net8.0 or net10.0 (default net10.0).
# Usage: ./scripts/macos/worktrees.sh [list | prune | remove <name> [--force] [--keep-branch]] [--output-format json]
set -e
TFM="${MUX_TFM:-net10.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
if [ $# -eq 0 ]; then set -- list; fi
dotnet run --project "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM" -- worktree "$@" --cwd "$PWD"

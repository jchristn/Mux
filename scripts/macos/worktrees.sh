#!/usr/bin/env bash
# List, prune, or remove the isolated git worktrees mux keeps for subagents and jobs (mux worktree), using the
# build from source. Runs against the repository containing the directory you launch it from.
# Prefix arguments with net8.0 or --framework net8.0; MUX_TFM is also supported.
# Usage: ./scripts/macos/worktrees.sh [list | prune | remove <name> [--force] [--keep-branch]] [--output-format json]
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
. "$SCRIPT_DIR/../common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
if [ $# -eq 0 ]; then set -- list; fi
dotnet run --project "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM" -- worktree "$@" --cwd "$PWD"

#!/usr/bin/env bash
# List, prune or remove the isolated git worktrees mux keeps for subagents and jobs (mux worktree),
# using the build from source. Runs against the repository containing your current directory.
# Usage: ./scripts/wsl/worktrees.sh [net8.0|net10.0] [list | prune | remove <name> [--force] [--keep-branch]] [--output-format json]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
if [ "$#" -eq 0 ]; then set -- list; fi
dotnet run --project "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM" -- worktree "$@" --cwd "$PWD"

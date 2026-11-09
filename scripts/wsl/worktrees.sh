#!/usr/bin/env sh
# List, prune, or remove the git worktrees mux keeps for subagents and jobs, using the build from
# source. WSL 1 counterpart of scripts/linux/worktrees.sh.
# Usage: ./scripts/wsl/worktrees.sh [list | prune | remove <name> [--force] [--keep-branch]] [--output-format json]
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl   # "wsl" in scripts/wsl, "wsl2" in scripts/wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd git "Install it inside WSL, e.g. sudo apt install git."
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_run "$SCRIPT_DIR/../linux/worktrees.sh" "$@"

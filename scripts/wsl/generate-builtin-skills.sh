#!/usr/bin/env sh
# Regenerate BUILTIN_SKILLS.md from the default skill library and the bundled packs.
# WSL 1 counterpart of scripts/linux/generate-builtin-skills.sh.
# Usage: ./scripts/wsl/generate-builtin-skills.sh [net8.0|net10.0] [--framework TFM] [--check] [--no-build]
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
wsl_require_cmd python3 "Install it inside WSL, e.g. sudo apt install python3."
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_run "$SCRIPT_DIR/../linux/generate-builtin-skills.sh" "$@"

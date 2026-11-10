#!/usr/bin/env bash
# Regenerate BUILTIN_SKILLS.md from the default skill library and the bundled packs.
#   --check     exit 1 when BUILTIN_SKILLS.md is out of date instead of rewriting it
#   --no-build  reuse the existing Debug build of Mux.Cli for the selected framework
# Usage: ./scripts/wsl2/generate-builtin-skills.sh [net8.0|net10.0] [--framework TFM] [--check] [--no-build]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

wsl_require_cmd python3 "Install Python 3 inside WSL (sudo apt install python3)."
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
exec python3 "$REPO_ROOT/scripts/common/generate-builtin-skills.py" "$@"

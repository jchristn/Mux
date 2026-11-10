#!/usr/bin/env bash
# Regenerate BUILTIN_SKILLS.md from the default skill library and the bundled packs.
#   --check     exit 1 when BUILTIN_SKILLS.md is out of date instead of rewriting it
#   --no-build  reuse the existing Debug build of Mux.Cli for the selected framework
# Usage: ./scripts/macos/generate-builtin-skills.sh [net8.0|net10.0] [--framework TFM] [--check] [--no-build]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

command -v python3 >/dev/null 2>&1 || die "python3 was not found on PATH. Install Python 3."
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
exec python3 "$REPO_ROOT/scripts/common/generate-builtin-skills.py" "$@"

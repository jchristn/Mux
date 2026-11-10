#!/usr/bin/env bash
# Regenerate BUILTIN_SKILLS.md from the default skill library and the bundled packs.
#   --check     exit 1 when BUILTIN_SKILLS.md is out of date instead of rewriting it
#   --no-build  reuse the existing Debug build of Mux.Cli for the selected framework
# Usage: ./scripts/linux/generate-builtin-skills.sh [net8.0|net10.0] [--framework TFM] [--check] [--no-build]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Linux ]; then
    die "This script is for Linux. Use scripts/macos on macOS or scripts\\windows on Windows."
fi
# WSL, not just a Microsoft kernel: Docker Desktop containers on the WSL 2 backend have one too.
if grep -qi microsoft /proc/version 2>/dev/null && { [ -n "${WSL_DISTRO_NAME:-}" ] || [ -d /run/WSL ] || ls /proc/sys/fs/binfmt_misc/WSLInterop* >/dev/null 2>&1; }; then
    die "This is WSL. Use the same script in scripts/wsl2 (WSL 2) or scripts/wsl (WSL 1); they handle the Windows PATH, line endings and Windows startup."
fi
# shellcheck source=../common/native-libs.sh
. "$REPO_ROOT/scripts/common/native-libs.sh"

command -v python3 >/dev/null 2>&1 || die "python3 was not found on PATH. Install Python 3."
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://learn.microsoft.com/dotnet/core/install/linux"
native_check_icu
exec python3 "$REPO_ROOT/scripts/common/generate-builtin-skills.py" "$@"

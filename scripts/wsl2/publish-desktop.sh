#!/usr/bin/env sh
# Publish a self-contained, single-file mux Desktop build. WSL 2 counterpart of
# scripts/linux/publish-desktop.sh. The default RID is linux-x64/linux-arm64; pass win-x64 to
# cross-publish a Windows build into dist/desktop/win-x64.
# Usage: ./scripts/wsl2/publish-desktop.sh [RID] [TFM]
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2   # "wsl" in scripts/wsl, "wsl2" in scripts/wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_run "$SCRIPT_DIR/../linux/publish-desktop.sh" "$@"

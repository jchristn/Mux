#!/usr/bin/env sh
# Launch the mux desktop application (Avalonia). WSL 1 counterpart of scripts/linux/run-desktop.sh.
# It is a GUI app: WSL 2 needs WSLg; WSL 1 cannot show it.
# Usage: ./scripts/wsl/run-desktop.sh [net8.0|net10.0]
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
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# WSL 1 has no WSLg, so GUI apps cannot open a window unless an X server runs on Windows.
if [ "$(wsl_version)" = 1 ] && [ -z "${DISPLAY:-}" ]; then
    wsl_die "WSL 1 cannot show mux Desktop (no WSLg). Run scripts\\windows\\run-desktop.bat from Windows, or move this distro to WSL 2 (wsl --set-version ${WSL_DISTRO_NAME:-<distro>} 2) and use scripts/wsl2."
fi
if [ -z "${WAYLAND_DISPLAY:-}" ] && [ -z "${DISPLAY:-}" ]; then
    wsl_die "No display found. Mux Desktop is a GUI app and needs WSLg (Windows 11, or Windows 10 with the Microsoft Store WSL). Run wsl --update and wsl --shutdown from Windows, then reopen this shell."
fi
[ -d /mnt/wslg ] || wsl_log "Note: WSLg was not detected; using DISPLAY=${DISPLAY:-} (an X server on Windows?)."
wsl_run "$SCRIPT_DIR/../linux/run-desktop.sh" "$@"

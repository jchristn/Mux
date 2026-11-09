#!/usr/bin/env sh
# Build and install the mux global tool (Mux.Cli). WSL 2 counterpart of scripts/linux/install-tool.sh.
# Uses the Linux dotnet (never the Windows one), puts ~/.dotnet/tools on PATH for the post-install
# 'mux -v' check, and prints what to add to your shell profile when new shells will not find mux.
# Usage: ./scripts/wsl2/install-tool.sh [net8.0|net10.0]
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2   # "wsl" in scripts/wsl, "wsl2" in scripts/wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
ORIGINAL_DOTNET_ROOT="${DOTNET_ROOT:-}"
# Create ~/.dotnet/tools first so wsl_sanitize_path puts it on PATH and the post-install
# 'mux -v' check in the Linux script finds the new tool.
mkdir -p "$HOME/.dotnet/tools"
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# Not exec: the PATH hint below runs after the Linux script.
bash "$SCRIPT_DIR/../linux/install-tool.sh" "$@"

# Tell the user what new shells need: 'dotnet tool install -g' puts mux in ~/.dotnet/tools, and a
# user-local .NET (~/.dotnet) also needs DOTNET_ROOT so the mux launcher can find the runtime.
case ":$WSL_ORIGINAL_PATH:" in
    *":$HOME/.dotnet/tools:"*) ;;
    *)
        wsl_log "Note: ~/.dotnet/tools is not on your PATH, so new WSL shells will not find 'mux'."
        wsl_log "Add this line to ~/.bashrc (or ~/.profile):"
        wsl_log '  export PATH="$PATH:$HOME/.dotnet/tools"' ;;
esac
if [ -z "$ORIGINAL_DOTNET_ROOT" ] && [ "${DOTNET_ROOT:-}" = "$HOME/.dotnet" ]; then
    wsl_log "Note: .NET is installed in ~/.dotnet, so 'mux' also needs DOTNET_ROOT. Add to ~/.bashrc:"
    wsl_log '  export DOTNET_ROOT="$HOME/.dotnet"'
fi

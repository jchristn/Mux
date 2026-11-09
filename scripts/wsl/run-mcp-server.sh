#!/usr/bin/env sh
# Build mux from source and serve it as an MCP server (mux mcp serve). WSL 1 counterpart of
# scripts/linux/run-mcp-server.sh, with the Windows PATH dropped and ~/.dotnet on PATH. Arguments are
# passed to `mux mcp serve`. Nothing here writes to stdout before exec (stdout is the stdio MCP
# stream); the WSL checks and the build log go to stderr.
# Usage: ./scripts/wsl/run-mcp-server.sh [net8.0|net10.0] [--http <port>] [--host <name>] [...]
# Register with Claude Code installed inside WSL:
#   claude mcp add mux -- /path/to/mux/scripts/wsl/run-mcp-server.sh
# Register with Claude Code on Windows (runs the server in this distro):
#   claude mcp add mux -- wsl.exe -d <distro> --exec /path/to/mux/scripts/wsl/run-mcp-server.sh
# Over --http, Windows clients reach the server on localhost through WSL localhost forwarding; if
# they cannot, pass --host 0.0.0.0 (with --api-key) or enable networkingMode=mirrored in .wslconfig.
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
wsl_run "$SCRIPT_DIR/../linux/run-mcp-server.sh" "$@"

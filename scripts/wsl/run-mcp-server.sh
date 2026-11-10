#!/usr/bin/env bash
# Build mux from source and serve it as an MCP server (mux mcp serve), for MCP clients such as
# Claude Code, Codex or mux itself. Arguments after the optional framework go to `mux mcp serve`, so
# the defaults are stdio transport and the deny approval ceiling. Nothing is written to stdout before
# the server starts (stdout is the MCP stream over stdio); the build log goes to stderr.
# Usage: ./scripts/wsl/run-mcp-server.sh [net8.0|net10.0] [--http <port>] [--host <name>] [--api-key <key>] [--allow-skills]
#        [--log-messages] [--approval-policy deny|auto-safe|auto] [--endpoint <name>] [-w <dir>]
# Register it with Claude Code: claude mcp add mux -- /path/to/mux/scripts/wsl/run-mcp-server.sh
# (use a Claude Code installed inside this distro). From Claude Code on Windows, register:
#   claude mcp add mux -- wsl.exe -d <distro> --exec /path/to/mux/scripts/wsl/run-mcp-server.sh
# Over --http, Windows clients reach the server on localhost: WSL 1 shares the Windows network.
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
dotnet build "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM" -nologo -v quiet >&2
exec dotnet "$REPO_ROOT/src/Mux.Cli/bin/Debug/$TFM/Mux.Cli.dll" mcp serve "$@"

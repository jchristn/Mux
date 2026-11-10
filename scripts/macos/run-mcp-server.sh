#!/usr/bin/env bash
# Build mux from source and serve it as an MCP server (mux mcp serve), for MCP clients such as
# Claude Code, Codex or mux itself. Arguments after the optional framework go to `mux mcp serve`, so
# the defaults are stdio transport and the deny approval ceiling. Nothing is written to stdout before
# the server starts (stdout is the MCP stream over stdio); the build log goes to stderr.
# Usage: ./scripts/macos/run-mcp-server.sh [net8.0|net10.0] [--http <port>] [--host <name>] [--api-key <key>] [--allow-skills]
#        [--log-messages] [--approval-policy deny|auto-safe|auto] [--endpoint <name>] [-w <dir>]
# Register it with Claude Code: claude mcp add mux -- /path/to/mux/scripts/macos/run-mcp-server.sh
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
dotnet build "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM" -nologo -v quiet >&2
exec dotnet "$REPO_ROOT/src/Mux.Cli/bin/Debug/$TFM/Mux.Cli.dll" mcp serve "$@"

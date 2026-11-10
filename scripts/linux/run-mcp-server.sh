#!/usr/bin/env bash
# Build mux from source and serve it as an MCP server (mux mcp serve), for MCP clients such as
# Claude Code, Codex or mux itself. Arguments after the optional framework go to `mux mcp serve`, so
# the defaults are stdio transport and the deny approval ceiling. Nothing is written to stdout before
# the server starts (stdout is the MCP stream over stdio); the build log goes to stderr.
# Usage: ./scripts/linux/run-mcp-server.sh [net8.0|net10.0] [--http <port>] [--host <name>] [--api-key <key>] [--allow-skills]
#        [--log-messages] [--approval-policy deny|auto-safe|auto] [--endpoint <name>] [-w <dir>]
# Register it with Claude Code: claude mcp add mux -- /path/to/mux/scripts/linux/run-mcp-server.sh
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

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://learn.microsoft.com/dotnet/core/install/linux"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
dotnet build "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM" -nologo -v quiet >&2
exec dotnet "$REPO_ROOT/src/Mux.Cli/bin/Debug/$TFM/Mux.Cli.dll" mcp serve "$@"

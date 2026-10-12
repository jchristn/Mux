#!/usr/bin/env bash
# Build mux from source and serve it as an MCP server (mux mcp serve), for MCP clients such as Claude Code,
# Codex, or mux itself. Remaining arguments are passed to `mux mcp serve`, so the defaults are stdio transport and the
# deny approval ceiling. The build runs quietly first because stdout is the MCP protocol stream over stdio.
# Prefix arguments with net8.0 or --framework net8.0; MUX_TFM is also supported.
# Usage: ./scripts/linux/run-mcp-server.sh [--http <port>] [--host <name>] [--api-key <key>] [--allow-skills]
#        [--log-messages] [--approval-policy deny|auto-safe|auto] [--endpoint <name>] [-w <dir>]
# Example client registration: claude mcp add mux -- /path/to/mux/scripts/linux/run-mcp-server.sh
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
. "$SCRIPT_DIR/../common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
dotnet build "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM" -nologo -v quiet >&2
exec dotnet "$REPO_ROOT/src/Mux.Cli/bin/Debug/$TFM/Mux.Cli.dll" mcp serve "$@"

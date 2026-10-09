#!/usr/bin/env bash
# Build mux from source and serve it as an MCP server (mux mcp serve), for MCP clients such as Claude Code,
# Codex, or mux itself. Every argument is passed to `mux mcp serve`, so the defaults are stdio transport and the
# deny approval ceiling. The build runs quietly first because stdout is the MCP protocol stream over stdio.
# Set MUX_TFM to net8.0 or net10.0 (default net10.0).
# Usage: ./scripts/macos/run-mcp-server.sh [--http <port>] [--host <name>] [--api-key <key>] [--allow-skills]
#        [--log-messages] [--approval-policy deny|auto-safe|auto] [--endpoint <name>] [-w <dir>]
# Example client registration: claude mcp add mux -- /path/to/mux/scripts/macos/run-mcp-server.sh
set -e
TFM="${MUX_TFM:-net10.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
dotnet build "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --framework "$TFM" -nologo -v quiet >&2
exec dotnet "$REPO_ROOT/src/Mux.Cli/bin/Debug/$TFM/Mux.Cli.dll" mcp serve "$@"

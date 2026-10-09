#!/usr/bin/env bash
# Build the mux VS Code extension (src/Mux.VSCode) into a .vsix package.
# Usage: ./build-vscode.sh
# The .vsix is written to src/Mux.VSCode/ and named from the "name" and "version" in its package.json.
# Compilation runs automatically through the extension's vscode:prepublish script.
set -e
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXTDIR="$REPO_ROOT/src/Mux.VSCode"

if ! command -v npm >/dev/null 2>&1; then
  echo "npm not found. Install Node.js and try again." >&2
  exit 1
fi

echo "Building the mux VS Code extension"
echo "  project: $EXTDIR"
echo

cd "$EXTDIR"
echo "Installing dependencies..."
npm ci

echo
echo "Packaging..."
npx --yes @vscode/vsce package

VSIX="$(ls -t "$EXTDIR"/*.vsix | head -1)"
echo
echo "Done: $VSIX"
echo "Install with: code --install-extension \"$VSIX\""
echo "  (or in VS Code: Extensions > ... > Install from VSIX...)"

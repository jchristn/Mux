#!/usr/bin/env bash
# Publish the mux VS Code extension (src/Mux.VSCode) to the VS Code Marketplace.
# The token is a Marketplace Personal Access Token (Azure DevOps, scope "Marketplace: Manage",
# organization "All accessible organizations"); see PUBLISH_VSCODE.md. Without an argument the
# script prompts for it. Publishes the version in src/Mux.VSCode/package.json; bump it to re-publish.
# Usage: ./scripts/wsl2/publish-vscode.sh [marketplace-token]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

case "${1:-}" in
    -h|--help) echo "Usage: ./scripts/wsl2/publish-vscode.sh [marketplace-token]"; exit 0 ;;
    -*) die "Unknown option: $1" ;;
esac
[ "$#" -le 1 ] || die "Unexpected argument: $2"
TOKEN="${1:-}"
if [ -z "$TOKEN" ] && [ -t 0 ]; then
    read -r -s -p "Enter your VS Code Marketplace token: " TOKEN
    echo
fi
[ -n "$TOKEN" ] || die "No token provided. Aborting."
wsl_require_cmd npm "Install Node.js inside WSL (it must be the Linux node, not the Windows one)."
wsl_require_cmd npx "Install Node.js inside WSL (it must be the Linux node, not the Windows one)."
EXTDIR="$REPO_ROOT/src/Mux.VSCode"
echo "Publishing the mux VS Code extension"
echo "  project: $EXTDIR"
echo
cd "$EXTDIR"
echo "Installing dependencies..."
npm install
echo
echo "Publishing to the VS Code Marketplace..."
if ! npx --yes @vscode/vsce publish -p "$TOKEN"; then
    echo >&2
    echo "Publish FAILED. See the error above and PUBLISH_VSCODE.md for troubleshooting:" >&2
    echo "  401                     - token scope/org wrong, or publisher owned by another account" >&2
    echo "  name/displayName taken  - change name/displayName in package.json" >&2
    echo "  version already exists  - bump \"version\" in src/Mux.VSCode/package.json" >&2
    exit 1
fi
echo
echo "Done. Published to the VS Code Marketplace:"
echo "  https://marketplace.visualstudio.com/items?itemName=usemux.mux-ai"

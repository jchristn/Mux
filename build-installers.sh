#!/usr/bin/env bash
# Build every installer this machine's OS can produce into installers/<version>/ (macOS: .pkg, .dmg, archives;
# Linux: .deb, .rpm, AppImage, archives). Native packagers only run on their own OS, so a full release needs all
# three; the release workflow (.github/workflows/release.yml) builds them on GitHub's runners when a v* tag is pushed.
#
# Usage: ./build-installers.sh [VERSION] [--framework net8.0|net10.0] [--dry-run]
# VERSION defaults to <Version> in src/Mux.Cli/Mux.Cli.csproj. Windows: build-installers.bat.
set -euo pipefail
ROOT="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
case "$(uname -s)" in
    Darwin) exec "$ROOT/scripts/macos/build-installers.sh" "$@" ;;
    Linux) exec "$ROOT/scripts/linux/build-installers.sh" "$@" ;;
    *) echo "Error: unsupported OS $(uname -s). On Windows run build-installers.bat." >&2; exit 1 ;;
esac

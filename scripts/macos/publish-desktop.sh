#!/usr/bin/env bash
# Publish a self-contained, single-file mux Desktop build for one runtime.
#
# RID  defaults to this machine (osx-arm64 or osx-x64). Pass another RID to cross-publish.
# TFM  defaults to MUX_TFM, then net10.0 with a .NET 10 SDK, otherwise net8.0.
#
# Output lands in dist/desktop/<RID>/. The build is self-contained, so the target machine does NOT
# need the .NET runtime. Artifacts are UNSIGNED; see docs/DESKTOP.md ("Packaging") for signing.
# Usage: ./scripts/macos/publish-desktop.sh [RID] [net8.0|net10.0]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
PROJECT="$REPO_ROOT/src/Mux.Desktop/Mux.Desktop.csproj"

guess_rid() {
    case "$(uname -m)" in
        x86_64|amd64) echo "osx-x64" ;;
        arm64|aarch64) echo "osx-arm64" ;;
        *) die "Unsupported architecture $(uname -m); pass a RID explicitly." ;;
    esac
}

RID=""
# A first argument that is not a framework or an option is the RID.
if [ "$#" -gt 0 ]; then
    case "$1" in
        net*|--framework|-f|-h|--help) ;;
        -*) die "Unknown option: $1" ;;
        *) RID="$1"; shift ;;
    esac
fi
[ -n "$RID" ] || RID="$(guess_rid)"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
OUT="$REPO_ROOT/dist/desktop/$RID"

echo "Publishing mux Desktop"
echo "  project: $PROJECT"
echo "  runtime: $RID"
echo "  target:  $TFM"
echo "  output:  $OUT"
echo

dotnet publish "$PROJECT" \
    -c Release \
    -f "$TFM" \
    -p:TargetFrameworks="$TFM" \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none \
    -o "$OUT"

echo
echo "Done. Self-contained (unsigned) build in: $OUT"
echo "Distribute the whole folder; the launcher is 'Mux.Desktop' ('Mux.Desktop.exe' for win-* runtimes)."

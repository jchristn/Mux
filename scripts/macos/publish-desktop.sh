#!/usr/bin/env bash
# Publish a self-contained, single-file mux Desktop build for one runtime.
#
# Usage:
#   ./scripts/macos/publish-desktop.sh [RID] [TFM]
#
# RID  defaults to a guess from the host OS/arch (linux-x64, osx-arm64, win-x64, ...).
# TFM  defaults to MUX_TFM, then net10.0 with a .NET 10 SDK, otherwise net8.0.
#
# Output lands in dist/desktop/<RID>/. The build is self-contained: the target
# machine does NOT need the .NET runtime installed. Artifacts are UNSIGNED — see
# docs/DESKTOP.md ("Packaging") for the code-signing / notarization steps, which
# require your platform signing certificates.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="$REPO_ROOT/src/Mux.Desktop/Mux.Desktop.csproj"

# --- Resolve the runtime identifier ---------------------------------------
guess_rid() {
  local os arch
  case "$(uname -s)" in
    Linux*)  os="linux" ;;
    Darwin*) os="osx" ;;
    MINGW*|MSYS*|CYGWIN*) os="win" ;;
    *) os="linux" ;;
  esac
  case "$(uname -m)" in
    x86_64|amd64) arch="x64" ;;
    arm64|aarch64) arch="arm64" ;;
    *) arch="x64" ;;
  esac
  echo "${os}-${arch}"
}

RID="${1:-$(guess_rid)}"
# Preserve the existing [RID] [TFM] syntax, and allow a leading framework.
if [ "$#" -gt 0 ] && [ "$1" = "$RID" ]; then
  case "$1" in net*|--framework|-f|-h|--help) RID="$(guess_rid)" ;; *) shift ;; esac
fi
. "$SCRIPT_DIR/../common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || { echo "Unexpected argument: $1" >&2; exit 1; }
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
echo "Distribute the whole folder; the launcher is 'Mux.Desktop' (or 'Mux.Desktop.exe' on Windows)."

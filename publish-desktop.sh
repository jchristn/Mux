#!/usr/bin/env bash
# Publish a self-contained, single-file mux Desktop build for one runtime.
#
# Usage:
#   ./publish-desktop.sh [RID] [TFM]
#
# RID  defaults to a guess from the host OS/arch (linux-x64, osx-arm64, win-x64, ...).
# TFM  defaults to net10.0 (pass net8.0 for the LTS build).
#
# Output lands in dist/desktop/<RID>/. The build is self-contained: the target
# machine does NOT need the .NET runtime installed. Artifacts are UNSIGNED — see
# docs/DESKTOP.md ("Packaging") for the code-signing / notarization steps, which
# require your platform signing certificates.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/Mux.Desktop/Mux.Desktop.csproj"

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
TFM="${2:-net10.0}"
OUT="$SCRIPT_DIR/dist/desktop/$RID"

echo "Publishing mux Desktop"
echo "  project: $PROJECT"
echo "  runtime: $RID"
echo "  target:  $TFM"
echo "  output:  $OUT"
echo

dotnet publish "$PROJECT" \
  -c Release \
  -f "$TFM" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -o "$OUT"

echo
echo "Done. Self-contained (unsigned) build in: $OUT"
echo "Distribute the whole folder; the launcher is 'Mux.Desktop' (or 'Mux.Desktop.exe' on Windows)."

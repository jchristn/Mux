#!/usr/bin/env bash
# Build mux installers for THIS machine's operating system and drop them under
# installers/<version>/. Native installer formats are OS-locked — a .dmg can only be
# built on macOS, .deb/.rpm/AppImage on Linux — so this script builds the channels
# your current OS owns. Run it on each OS (and build-installers.bat on Windows) to get
# the full set, or just push a version tag to let the GitHub Actions matrix build all
# three at once (see .github/workflows/release.yml).
#
# Usage:
#   ./build-installers.sh [VERSION] [--dry-run]
#
# VERSION   defaults to the <Version> in src/Mux.Cli/Mux.Cli.csproj.
# --dry-run prints the plan (files + commands) without building or needing the packagers.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR"

# --- Parse arguments -------------------------------------------------------
VERSION=""
DRYRUN=""
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRYRUN="--dry-run" ;;
    -h|--help) sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) VERSION="$arg" ;;
  esac
done

# --- Resolve the version ---------------------------------------------------
if [ -z "$VERSION" ]; then
  VERSION="$(grep -oE '<Version>[^<]+</Version>' "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" 2>/dev/null \
    | head -n1 | sed -E 's/<\/?Version>//g')"
fi
if [ -z "$VERSION" ]; then
  echo "error: could not determine version. Pass it explicitly: ./build-installers.sh 0.10.0" >&2
  exit 1
fi

# --- Detect the host OS ----------------------------------------------------
case "$(uname -s)" in
  Darwin*) OS_JOB="macos" ;;
  Linux*)  OS_JOB="linux" ;;
  MINGW*|MSYS*|CYGWIN*)
    echo "error: this looks like Windows. Use build-installers.bat instead." >&2
    exit 1 ;;
  *) echo "error: unsupported OS '$(uname -s)'." >&2; exit 1 ;;
esac

OUT="$REPO_ROOT/installers/$VERSION"
STAGING="$REPO_ROOT/dist/staging"
PUBLISHER="$REPO_ROOT/src/Mux.Publisher/Mux.Publisher.csproj"
PUB_DLL="$REPO_ROOT/src/Mux.Publisher/bin/Release/net10.0/Mux.Publisher.dll"

echo "mux installers"
echo "  version : $VERSION"
echo "  host os : $OS_JOB"
echo "  output  : $OUT"
[ -n "$DRYRUN" ] && echo "  mode    : DRY RUN (no artifacts produced)"
echo

mkdir -p "$OUT"

# --- Build the orchestrator once -------------------------------------------
echo "Building Mux.Publisher..."
dotnet build "$PUBLISHER" -c Release -f net10.0 -v quiet -nologo || {
  echo "error: failed to build Mux.Publisher." >&2; exit 1;
}

# --- Enumerate and run this OS's channels, in dependency order -------------
CHANNELS="$(dotnet "$PUB_DLL" --channels-for "$OS_JOB" --repo "$REPO_ROOT")"
if [ -z "$CHANNELS" ]; then
  echo "No enabled channels for $OS_JOB in publisher.json."; exit 0
fi
echo "Channels for $OS_JOB: $CHANNELS"
echo

FAILED=""
for ch in $CHANNELS; do
  echo "==> $ch"
  if dotnet "$PUB_DLL" --channel "$ch" --version "$VERSION" --repo "$REPO_ROOT" --out "$OUT" --staging "$STAGING" $DRYRUN; then
    :
  else
    echo "    (channel '$ch' failed — is its packaging tool installed?)"
    FAILED="$FAILED $ch"
  fi
  echo
done

# --- Summary ---------------------------------------------------------------
echo "-------------------------------------------------------------------"
if [ -n "$DRYRUN" ]; then
  echo "Dry run complete for $OS_JOB (nothing built)."
else
  echo "Done. $OS_JOB installers are under: $OUT"
  find "$OUT" -maxdepth 2 -type f ! -path '*/publish/*' 2>/dev/null | sed "s|$OUT/|  |" | sort
fi
if [ -n "$FAILED" ]; then
  echo
  echo "Channels that failed:$FAILED"
  echo "Install the missing packagers (Linux: fpm, rpm, appimagetool, createrepo_c, dpkg-dev;"
  echo "macOS: Xcode command line tools) and re-run, or use --dry-run to preview."
fi
echo
echo "Note: this built only $OS_JOB installers. Run build-installers.sh on the other Unix OS"
echo "and build-installers.bat on Windows for the full set — or push a 'v$VERSION' tag to build"
echo "all three via GitHub Actions."
[ -z "$FAILED" ] || exit 1

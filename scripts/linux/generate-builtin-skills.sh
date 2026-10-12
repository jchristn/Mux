#!/usr/bin/env bash
# Regenerate BUILTIN_SKILLS.md from the default skill library and the bundled packs.
# Usage: ./scripts/<os>/generate-builtin-skills.sh [net8.0|net10.0] [--framework TFM] [--check] [--no-build]
#   --check     exit 1 when BUILTIN_SKILLS.md is out of date instead of rewriting it
#   --no-build  reuse the existing Debug build for the selected framework of Mux.Cli
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python3 "$SCRIPT_DIR/../common/generate-builtin-skills.py" "$@"

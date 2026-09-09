#!/usr/bin/env bash
# Upload all CehoProxy release assets to GitHub.
# Usage: scripts/upload-release-assets.sh 1.2.31 [publish]
#
# Expects publish/bundles/CehoProxy-<ver>-*.zip and bare binaries in publish/
# (see docs/AI-AGENT-PROMPT.md §6.7). Without bare binaries chp update fails.

set -euo pipefail

VER="${1:?version required, e.g. 1.2.31}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="${2:-$ROOT/publish}"
BUNDLES="$BIN/bundles"
REPO="${CEHOPROXY_RELEASE_REPO:-CodoCeh/CehoProxy}"
TAG="v$VER"

BARE=(
  cehoproxy-win-x64.exe
  cehoproxy-osx-arm64
  cehoproxy-osx-x64
  cehoproxy-linux-x64
  cehoproxy-linux-arm64
)

ZIPS=(
  "CehoProxy-$VER-windows.zip"
  "CehoProxy-$VER-macos-apple.zip"
  "CehoProxy-$VER-macos-intel.zip"
  "CehoProxy-$VER-linux-x64.zip"
  "CehoProxy-$VER-linux-arm64.zip"
)

missing=()
for f in "${BARE[@]}"; do
  [[ -f "$BIN/$f" ]] || missing+=("$BIN/$f")
done
for f in "${ZIPS[@]}"; do
  [[ -f "$BUNDLES/$f" ]] || missing+=("$BUNDLES/$f")
done
[[ -f "$BIN/CehoProxy.cmd" ]] || missing+=("$BIN/CehoProxy.cmd")

if ((${#missing[@]})); then
  echo "Missing release files:" >&2
  printf '  %s\n' "${missing[@]}" >&2
  exit 1
fi

upload=(gh release upload "$TAG" --repo "$REPO" --clobber)
for f in "${ZIPS[@]}"; do upload+=("$BUNDLES/$f"); done
for f in "${BARE[@]}"; do upload+=("$BIN/$f"); done
upload+=("$BIN/CehoProxy.cmd")

SETUP="$BIN/CehoProxy-Setup-$VER.exe"
[[ -f "$SETUP" ]] && upload+=("$SETUP")

echo "Uploading ${#upload[@]} paths to $REPO $TAG ..."
"${upload[@]}"
echo "OK. Check: https://github.com/$REPO/releases/download/$TAG/cehoproxy-win-x64.exe"

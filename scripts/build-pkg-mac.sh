#!/bin/bash
set -euo pipefail

# build-pkg-mac.sh <версия> <apple|intel> <папка с распакованным CehoProxy-<версия>-macos-*.zip> <куда>
VERSION="$1"; KIND="$2"; SRC="$3"; OUT="$4"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(dirname "$HERE")"
MAC="$HERE/installers/mac"

case "$KIND" in
  apple) HOSTARCH=arm64 ;;
  intel) HOSTARCH=x86_64 ;;
  *) echo "apple или intel"; exit 1 ;;
esac

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

STAGE="$WORK/payload/usr/local/share/cehoproxy-setup"
mkdir -p "$STAGE" "$WORK/res"
ditto --noextattr --norsrc --noqtn "$SRC/cehoproxy" "$STAGE/cehoproxy"
ditto --noextattr --norsrc --noqtn "$HERE/install.sh" "$STAGE/install.sh"
ditto --noextattr --norsrc --noqtn "$HERE/install-tray-mac.sh" "$STAGE/install-tray-mac.sh"
ditto --noextattr --norsrc --noqtn "$SRC/CehoProxy Tray.app" "$STAGE/CehoProxy Tray.app"
find "$WORK" -name ".DS_Store" -delete
xattr -cr "$STAGE" 2>/dev/null || true
chmod 755 "$STAGE/cehoproxy" "$STAGE/install.sh" "$STAGE/install-tray-mac.sh"

export COPYFILE_DISABLE=1
pkgbuild --root "$WORK/payload" --identifier ru.codoceh.cehoproxy --version "$VERSION" \
  --scripts "$MAC/scripts" --install-location / --ownership recommended --filter '/\._' "$WORK/component.pkg" >/dev/null

cp "$MAC/welcome.html" "$MAC/conclusion.html" "$WORK/res/"
cp "$REPO/LICENSE" "$WORK/res/LICENSE"
python3 - "$REPO/assets/cehoproxy.png" "$WORK/res/background.png" <<'PY'
import sys
from PIL import Image
logo = Image.open(sys.argv[1]).convert("RGBA").resize((120, 120), Image.LANCZOS)
bg = Image.new("RGBA", (120, 120), (16, 21, 18, 255))
bg.paste(logo, (0, 0), logo)
bg.save(sys.argv[2])
PY
sed -e "s/ARCH_PLACEHOLDER/$HOSTARCH/" -e "s/VERSION_PLACEHOLDER/$VERSION/" "$MAC/distribution.xml" > "$WORK/distribution.xml"

mkdir -p "$OUT"
productbuild --distribution "$WORK/distribution.xml" --resources "$WORK/res" --package-path "$WORK" \
  "$OUT/CehoProxy-$VERSION-macos-$KIND.pkg" >/dev/null
echo "$OUT/CehoProxy-$VERSION-macos-$KIND.pkg"

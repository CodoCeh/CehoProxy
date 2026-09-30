#!/bin/bash
set -euo pipefail

# make-installers.sh <версия> [--upload]
# Берёт готовые бинарники из релиза v<версия>, собирает установщики с окном для всех систем
# и кладёт рядом копии без номера версии: на них ссылается README (releases/latest/download/…).
VERSION="$1"; UPLOAD="${2:-}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(dirname "$HERE")"
TAG="v$VERSION"
WORK="$REPO/publish/_installers"
OUT="$HERE/Output/release"
ISCC="${ISCC:-$HOME/.wine/drive_c/Program Files (x86)/Inno Setup 6/ISCC.exe}"

rm -rf "$WORK" "$OUT"; mkdir -p "$WORK" "$OUT"

for try in 1 2 3 4; do gh release download "$TAG" -D "$WORK" --clobber \
  -p cehoproxy-win-x64.exe -p cehoproxy-tray-win-x64.exe -p libcronet.dll \
  -p cehoproxy-linux-x64 -p cehoproxy-linux-arm64 \
  -p "CehoProxy-$VERSION-macos-apple.zip" -p "CehoProxy-$VERSION-macos-intel.zip" \
  -p "CehoProxy-$VERSION-windows.zip" -p "CehoProxy-$VERSION-SHA256SUMS.txt" && break; sleep 5; done
[ -f "$WORK/libcronet.dll" ] || { echo "не скачался libcronet.dll"; exit 1; }

cp "$WORK/cehoproxy-win-x64.exe" "$REPO/publish/cehoproxy.exe"
cp "$WORK/cehoproxy-tray-win-x64.exe" "$REPO/publish/cehoproxy-tray.exe"
cp "$WORK/libcronet.dll" "$REPO/publish/libcronet.dll"
(cd "$HERE" && wine64 "$ISCC" /Q cehoproxy.iss 2>&1 | { grep -v fixme || true; } | tail -2)
[ -f "$HERE/Output/CehoProxy-Setup-$VERSION.exe" ] || { echo "Setup не собрался"; exit 1; }
cp "$HERE/Output/CehoProxy-Setup-$VERSION.exe" "$OUT/"

for kind in apple intel; do
  unzip -q -o "$WORK/CehoProxy-$VERSION-macos-$kind.zip" -d "$WORK/mac-$kind"
  "$HERE/build-pkg-mac.sh" "$VERSION" "$kind" "$WORK/mac-$kind/CehoProxy" "$OUT"
done

for kind in x64 arm64; do
  python3 "$HERE/build-deb.py" "$VERSION" "$kind" "$WORK/cehoproxy-linux-$kind" "$OUT"
done

cp "$WORK/CehoProxy-$VERSION-windows.zip" "$OUT/"

cd "$OUT"
for f in CehoProxy-*"$VERSION"*; do
  stable="$(printf '%s' "$f" | sed -e "s/-$VERSION//")"
  cp "$f" "$stable"
done
ls -l

if [ "$UPLOAD" = "--upload" ]; then
  cp "$WORK/CehoProxy-$VERSION-SHA256SUMS.txt" sums.txt
  for f in CehoProxy-Setup-"$VERSION".exe CehoProxy-"$VERSION"-macos-*.pkg CehoProxy-"$VERSION"-linux-*.deb; do
    grep -q "  $f\$" sums.txt || shasum -a 256 "$f" >> sums.txt
  done
  cp sums.txt "CehoProxy-$VERSION-SHA256SUMS.txt"
  rm sums.txt
  gh release upload "$TAG" --clobber \
    CehoProxy-Setup.exe CehoProxy-macos-apple.pkg CehoProxy-macos-intel.pkg \
    CehoProxy-linux-x64.deb CehoProxy-linux-arm64.deb CehoProxy-windows.zip \
    "CehoProxy-$VERSION-macos-apple.pkg" "CehoProxy-$VERSION-macos-intel.pkg" \
    "CehoProxy-$VERSION-linux-x64.deb" "CehoProxy-$VERSION-linux-arm64.deb" \
    "CehoProxy-Setup-$VERSION.exe" "CehoProxy-$VERSION-SHA256SUMS.txt"
fi

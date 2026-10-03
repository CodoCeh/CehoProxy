#!/bin/sh

set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="${1:-arm64}"
OUT="${2:-$ROOT/publish}"
APP="$OUT/CehoProxy Tray.app"
VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$ROOT/Directory.Build.props" | head -n1)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

swiftc -O -target "$ARCH-apple-macos12.0" \
  -o "$APP/Contents/MacOS/CehoProxyTray" \
  "$ROOT/src/ProxyCage.Tray.Mac/CehoProxyTray.swift"

cp "$ROOT/assets/cehoproxy.icns" "$APP/Contents/Resources/cehoproxy.icns"
cp "$ROOT/assets/tray"/cehoproxy-status-*.png "$APP/Contents/Resources/"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>CehoProxy Tray</string>
  <key>CFBundleDisplayName</key><string>CehoProxy</string>
  <key>CFBundleIdentifier</key><string>ru.codoceh.cehoproxy.tray</string>
  <key>CFBundleExecutable</key><string>CehoProxyTray</string>
  <key>CFBundleIconFile</key><string>cehoproxy</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true

echo "$APP"

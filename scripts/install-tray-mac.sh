#!/bin/sh

set -eu

FROM="${1:-$(dirname "$0")}"
APP_SRC="$FROM/CehoProxy Tray.app"
APP="/Applications/CehoProxy Tray.app"
LABEL=ru.codoceh.cehoproxy.tray

[ "$(uname -s)" = "Darwin" ] || exit 0
[ "$(id -u)" = "0" ] || { echo "Значок ставится вместе с программой, с правами администратора."; exit 1; }
[ -d "$APP_SRC" ] || { echo "Рядом нет «CehoProxy Tray.app» — значок пропущен."; exit 0; }

USER_NAME="${SUDO_USER:-}"
if [ -z "$USER_NAME" ] || [ "$USER_NAME" = "root" ]; then
  echo "Не видно, кто вошёл в систему, — значок пропущен."
  exit 0
fi

UID_NUM="$(id -u "$USER_NAME")"
USER_HOME="$(dscl . -read "/Users/${USER_NAME}" NFSHomeDirectory 2>/dev/null | awk '{print $2}')"
[ -n "$USER_HOME" ] || { echo "Не нашли домашнюю папку ${USER_NAME} — значок пропущен."; exit 0; }

launchctl bootout "gui/${UID_NUM}/${LABEL}" >/dev/null 2>&1 || true
pkill -f CehoProxyTray >/dev/null 2>&1 || true

rm -rf "$APP"
cp -R "$APP_SRC" "$APP"
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
chown -R "$USER_NAME" "$APP" 2>/dev/null || true

AGENTS="$USER_HOME/Library/LaunchAgents"
mkdir -p "$AGENTS"
cat > "$AGENTS/${LABEL}.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>${LABEL}</string>
  <key>ProgramArguments</key>
  <array><string>${APP}/Contents/MacOS/CehoProxyTray</string></array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><false/>
  <key>LimitLoadToSessionType</key><string>Aqua</string>
</dict>
</plist>
PLIST
chown "$USER_NAME" "$AGENTS/${LABEL}.plist"

launchctl bootstrap "gui/${UID_NUM}" "$AGENTS/${LABEL}.plist" >/dev/null 2>&1 || true
launchctl kickstart "gui/${UID_NUM}/${LABEL}" >/dev/null 2>&1 || true

echo "Значок состояния установлен: $APP — он уже в строке меню."

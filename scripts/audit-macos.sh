#!/bin/bash
# Проверка CehoProxy на macOS: изоляция, живучесть системы и самолечение.
# Запускать под root: sudo ./audit-macos.sh /путь/к/программе
#
# Скрипт трогает маршрутизацию, поэтому у него есть сторож: что бы ни случилось,
# через AUDIT_TIMEOUT секунд защита выключается и следы снимаются без участия человека.

set -u

# ищем программу там же, где её мог поставить install.sh, и в PATH
if [ -z "${CEHO:-}" ]; then
  for c in /usr/local/bin/cehoproxy "$HOME/.local/bin/cehoproxy" \
           "$(eval echo ~${SUDO_USER:-$USER})/.local/bin/cehoproxy" "$(command -v cehoproxy || true)"; do
    [ -n "$c" ] && [ -x "$c" ] && CEHO="$c" && break
  done
fi
CEHO="${CEHO:-cehoproxy}"
APP="${1:-}"
AUDIT_TIMEOUT="${AUDIT_TIMEOUT:-180}"
PROBE_URL="${PROBE_URL:-http://ip-api.com/json}"

fail() { echo "ПРОВАЛ: $*"; FAILED=$((FAILED + 1)); }
ok()   { echo "ок: $*"; }
FAILED=0

[ "$(id -u)" = "0" ] || { echo "нужен root: sudo $0 <путь к программе>"; exit 1; }
[ -x "$CEHO" ] || {
  echo "Программа не найдена. Поставьте её:"
  echo "  sudo ./scripts/install.sh ./cehoproxy"
  echo "либо укажите путь: sudo CEHO=/путь/к/cehoproxy $0 $*"
  exit 1
}
echo "программа: $CEHO"

if [ -z "$APP" ]; then
  # копия системного бинарника на macOS убивается ядром: подписанные платформенные
  # программы не запускаются вне своего места. Поэтому берём что-то не системное.
  for c in /opt/homebrew/opt/curl/bin/curl /usr/local/opt/curl/bin/curl; do
    [ -x "$c" ] && APP="$c" && break
  done
fi
[ -n "$APP" ] || { echo "укажи программу для проверки: sudo $0 /Applications/Имя.app"; exit 1; }

exit_ip() { /usr/bin/curl -s --max-time 12 "$PROBE_URL" | sed -n 's/.*"query":"\([^"]*\)".*/\1/p'; }

cleanup() {
  "$CEHO" stop >/dev/null 2>&1
  sleep 3
  pkill -9 -f "sing-box run" >/dev/null 2>&1
  "$CEHO" remove-app "$APP" >/dev/null 2>&1
}
( sleep "$AUDIT_TIMEOUT"; cleanup ) &
WATCHDOG=$!
trap 'kill $WATCHDOG 2>/dev/null; cleanup' EXIT

echo "=== 0. исходное состояние"
BEFORE_IP=$(exit_ip); echo "IP системы без защиты: ${BEFORE_IP:-не определился}"
"$CEHO" add-app "$APP" | head -2

echo
echo "=== 1. запуск"
"$CEHO" daemon >/tmp/ceho-audit.log 2>&1 &
sleep 15
"$CEHO" status

echo
echo "=== 2. система работает при поднятом туннеле"
SYS_IP=$(exit_ip)
[ -n "$SYS_IP" ] && ok "система в сети, IP $SYS_IP" || fail "система потеряла сеть"
/usr/bin/dscacheutil -q host -a name example.com >/dev/null 2>&1 && ok "DNS отвечает" || fail "DNS сломан"
[ "$SYS_IP" = "$BEFORE_IP" ] && ok "IP системы не изменился — туннель её не перехватил" \
  || fail "IP системы изменился: было $BEFORE_IP, стало $SYS_IP"

echo
echo "=== 3. изолированное приложение идёт через туннель"
APP_IP=$("$APP" -s --max-time 15 "$PROBE_URL" 2>/dev/null | sed -n 's/.*"query":"\([^"]*\)".*/\1/p')
echo "IP приложения: ${APP_IP:-не определился}"
if [ -n "$APP_IP" ] && [ "$APP_IP" != "$SYS_IP" ]; then
  ok "приложение выходит другим адресом — изоляция работает"
else
  fail "адрес приложения совпал с системным или не определился"
fi
"$APP" -s --max-time 30 -o /dev/null "$PROBE_URL" &
sleep 3
"$CEHO" verify

echo
echo "=== 4. авария: kill -9 движку"
pkill -9 -f "sing-box run"; sleep 4
LEFT_IF=$(ifconfig -l | tr ' ' '\n' | grep -c '^utun')
ROUTES=$(netstat -rn -f inet | grep -c '172\.19\.0')
echo "интерфейсов utun в системе: $LEFT_IF, маршрутов в нашу подсеть: $ROUTES"
[ "$ROUTES" = "0" ] && ok "маршруты туннеля ядро сняло само" || fail "остались маршруты на мёртвый интерфейс"
CRASH_IP=$(exit_ip)
[ -n "$CRASH_IP" ] && ok "после аварии система в сети" || fail "после аварии система без сети"

echo
echo "=== 5. самолечение: повторный запуск"
"$CEHO" stop >/dev/null 2>&1; sleep 3
"$CEHO" daemon >/tmp/ceho-audit2.log 2>&1 &
sleep 15
"$CEHO" status | head -1
AFTER_IP=$("$APP" -s --max-time 15 "$PROBE_URL" 2>/dev/null | sed -n 's/.*"query":"\([^"]*\)".*/\1/p')
[ -n "$AFTER_IP" ] && ok "после аварии защита поднялась сама, IP приложения $AFTER_IP" \
  || fail "защита не поднялась после аварии"

echo
echo "=== 6. остановка и чистота"
"$CEHO" stop >/dev/null 2>&1; sleep 5
ROUTES=$(netstat -rn -f inet | grep -c '172\.19\.0')
[ "$ROUTES" = "0" ] && ok "после остановки маршрутов не осталось" || fail "после остановки остались маршруты"
FINAL_IP=$(exit_ip)
[ -n "$FINAL_IP" ] && ok "система в сети, IP $FINAL_IP" || fail "после остановки система без сети"

echo
if [ "$FAILED" = "0" ]; then echo "ИТОГ: всё сошлось, замечаний нет"; else echo "ИТОГ: провалов $FAILED"; fi
exit "$FAILED"

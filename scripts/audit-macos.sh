#!/bin/bash

set -u

if [ -z "${CEHO:-}" ]; then
  for c in /usr/local/bin/cehoproxy "$HOME/.local/bin/cehoproxy" \
           "$(eval echo ~${SUDO_USER:-$USER})/.local/bin/cehoproxy" "$(command -v cehoproxy || true)"; do
    [ -n "$c" ] && [ -x "$c" ] && CEHO="$c" && break
  done
fi
CEHO="${CEHO:-cehoproxy}"
APP="${1:-}"
AUDIT_TIMEOUT="${AUDIT_TIMEOUT:-300}"
PROBE_URL="${PROBE_URL:-http://ip-api.com/json}"
PROBE_URL6="${PROBE_URL6:-https://api64.ipify.org}"
TUN_IP="${TUN_IP:-172.31.211.1}"
GUARD_IP="${GUARD_IP:-172.31.211.5}"

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
  for c in /opt/homebrew/opt/curl/bin/curl /usr/local/opt/curl/bin/curl; do
    [ -x "$c" ] && APP="$c" && break
  done
fi
[ -n "$APP" ] || { echo "укажи программу для проверки: sudo $0 /Applications/Имя.app"; exit 1; }

exit_ip() { /usr/bin/curl -s --max-time 12 "$PROBE_URL" | sed -n 's/.*"query":"\([^"]*\)".*/\1/p'; }
exit_ip6() { /usr/bin/curl -6 -s --max-time 12 "$PROBE_URL6"; }
app_ip() { "$APP" -s --max-time "${1:-15}" "$PROBE_URL" 2>/dev/null | sed -n 's/.*"query":"\([^"]*\)".*/\1/p'; }
app_ip6() { "$APP" -6 -s --max-time "${1:-15}" "$PROBE_URL6" 2>/dev/null; }
tun_addresses() { /sbin/ifconfig | sed -n 's/^[[:space:]]*inet6\{0,1\} \([^ ]*\).*/\1/p'; }
has_address() { tun_addresses | grep -qx "$1"; }

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
BEFORE_IP6=$(exit_ip6); echo "IPv6 системы без защиты: ${BEFORE_IP6:-нет IPv6}"
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

if [ -n "$BEFORE_IP6" ]; then
  SYS_IP6=$(exit_ip6)
  [ -n "$SYS_IP6" ] && ok "IPv6 системы жив при поднятом туннеле: $SYS_IP6" \
    || fail "IPv6 системы пропал при поднятом туннеле (было $BEFORE_IP6)"
else
  echo "пропуск: у этой машины нет выхода по IPv6, проверять нечего"
  SYS_IP6=""
fi

echo
echo "=== 3. изолированное приложение идёт через туннель"
APP_IP=$(app_ip)
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
echo "=== 3b. IPv6 изолированного приложения не идёт мимо туннеля"
if [ -n "$SYS_IP6" ]; then
  APP_IP6=$(app_ip6)
  echo "IPv6 приложения: ${APP_IP6:-нет ответа}"
  if [ -z "$APP_IP6" ]; then
    ok "приложению IPv6 наружу не дали — утечки нет"
  elif [ "$APP_IP6" = "$SYS_IP6" ]; then
    fail "приложение вышло по IPv6 напрямую: $APP_IP6 — это адрес системы"
  else
    ok "IPv6 приложения отличается от системного: $APP_IP6"
  fi
else
  echo "пропуск: у машины нет IPv6"
fi

echo
echo "=== 4. авария: kill -9 движку, прямого выхода быть не должно"
pkill -9 -f "sing-box run"
LEAKED=""
for i in $(seq 1 15); do
  CRASH_APP_IP=$(app_ip 2)
  if [ -n "$CRASH_APP_IP" ] && [ "$CRASH_APP_IP" = "$SYS_IP" ]; then
    LEAKED="$CRASH_APP_IP"
    break
  fi
done
[ -z "$LEAKED" ] && ok "за время аварии приложение ни разу не вышло системным адресом" \
  || fail "приложение вышло напрямую: $LEAKED"
has_address "$GUARD_IP" && ok "поднялся запирающий туннель $GUARD_IP" \
  || echo "запирающего туннеля нет — защита уже восстановилась сама"
CRASH_IP=$(exit_ip)
[ -n "$CRASH_IP" ] && ok "после аварии система в сети" || fail "после аварии система без сети"

echo
echo "=== 5. самолечение: повторный запуск"
"$CEHO" stop >/dev/null 2>&1; sleep 3
"$CEHO" daemon >/tmp/ceho-audit2.log 2>&1 &
sleep 15
"$CEHO" status | head -1
AFTER_IP=$(app_ip)
[ -n "$AFTER_IP" ] && ok "после аварии защита поднялась сама, IP приложения $AFTER_IP" \
  || fail "защита не поднялась после аварии"

echo
echo "=== 6. остановка и чистота"
"$CEHO" stop >/dev/null 2>&1; sleep 5
has_address "$TUN_IP" && fail "после остановки остался адрес туннеля $TUN_IP" \
  || ok "адреса туннеля в системе нет"
has_address "$GUARD_IP" && fail "после остановки остался запирающий туннель $GUARD_IP" \
  || ok "запирающего туннеля в системе нет"
ROUTES=$(netstat -rn -f inet | grep -c "${TUN_IP%.*}")
[ "$ROUTES" = "0" ] && ok "после остановки маршрутов не осталось" || fail "после остановки остались маршруты"
FINAL_IP=$(exit_ip)
[ -n "$FINAL_IP" ] && ok "система в сети, IP $FINAL_IP" || fail "после остановки система без сети"
if [ -n "$BEFORE_IP6" ]; then
  FINAL_IP6=$(exit_ip6)
  [ -n "$FINAL_IP6" ] && ok "IPv6 системы вернулся: $FINAL_IP6" || fail "после остановки система без IPv6"
fi

echo
if [ "$FAILED" = "0" ]; then echo "ИТОГ: всё сошлось, замечаний нет"; else echo "ИТОГ: провалов $FAILED"; fi
exit "$FAILED"

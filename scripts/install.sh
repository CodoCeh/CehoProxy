#!/bin/sh

set -eu

REPO="${CEHOPROXY_REPO:-CodoCeh/CehoProxy}"
BIN=/usr/local/bin/cehoproxy
SRC="${1:-}"

[ "$(id -u)" = "0" ] || { echo "Нужны права администратора. Повторите с sudo."; exit 1; }

case "$(uname -s)" in
  Darwin) OS=osx;   ROOT="/Library/Application Support/CehoProxy" ;;
  Linux)  OS=linux; ROOT="/var/lib/cehoproxy" ;;
  *) echo "Поддерживаются Linux и macOS."; exit 1 ;;
esac

case "$(uname -m)" in
  arm64|aarch64) ARCH=arm64 ;;
  x86_64|amd64)  ARCH=x64 ;;
  *) echo "Неизвестная архитектура: $(uname -m)"; exit 1 ;;
esac

ASSET="cehoproxy-$OS-$ARCH"

if [ -z "$SRC" ]; then
  command -v curl >/dev/null 2>&1 || { echo "Нужен curl."; exit 1; }
  TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT

  echo "Скачиваю $ASSET из релизов $REPO…"
  URL="https://github.com/$REPO/releases/latest/download/$ASSET"
  if ! curl -fsSL "$URL" -o "$TMP/cehoproxy"; then
    echo
    echo "Скачать не удалось: $URL"
    echo "Так бывает, если релизов ещё нет или репозиторий закрыт."
    echo "Тогда соберите программу сами и повторите с путём к файлу:"
    echo "  sudo $0 ./cehoproxy"
    exit 1
  fi
  SRC="$TMP/cehoproxy"
fi

[ -f "$SRC" ] || { echo "Не найден файл программы: $SRC"; exit 1; }

# Старая версия останавливается ДО замены файла. Иначе на машине остаётся
# работающий прежний экземпляр, и непонятно, чьи правила действуют.
if [ -x "$BIN" ]; then
  OLD="$("$BIN" version 2>/dev/null | head -n1 || true)"
  [ -n "$OLD" ] && echo "Была установлена версия $OLD — заменяю её."
  "$BIN" stop >/dev/null 2>&1 || true
fi

install -m 755 "$SRC" "$BIN"
mkdir -p "$ROOT"
chmod 755 "$ROOT"

ln -sf "$BIN" /usr/local/bin/chp

# Регистрация в системе, затирание файлов прошлой сборки и возврат автозапуска.
# Настройки и сохранённые подписки эта команда не трогает.
# Движок ставится тут же: без него туннель не поднимется, а искать его руками — лишний шаг.
"$BIN" install --no-setup --with-engine

echo
echo "Страница продукта: https://github.com/$REPO"
echo "Программа: $BIN"
echo "Короткая команда: chp"
echo "Настройки и подписки: $ROOT (сохранены)"
echo

# Движок обычно уже скачан строкой выше. Сюда попадаем, если не получилось —
# тогда на экране должна остаться одна команда, а не рассуждение про пакетные менеджеры.
if ! command -v sing-box >/dev/null 2>&1 && [ ! -x "$ROOT/sing-box" ]; then
  echo "Движок sing-box скачать не удалось, без него туннель не поднимется."
  echo "Повторить одной командой:"
  echo "  sudo chp engine"
  echo
fi

# Настройку задаём только на чистой машине: при обновлении переспрашивать нечего.
if [ -f "$ROOT/config.json" ]; then
  echo "Обновление завершено, прежние настройки на месте."
  echo "  chp             # состояние"
  echo "  chp subs        # подписки, сроки и трафик"
  echo "  chp log         # журнал и падения"
elif { : < /dev/tty; } 2>/dev/null; then
  "$BIN" setup < /dev/tty
else
  echo "Терминала для вопросов нет, поэтому настройка не запущена."
  echo "Короткая команда chp уже добавлена. Дальше:"
  echo "  sudo chp setup      # язык, подписка, программы, автозапуск"
  echo "  chp                 # состояние и что делать дальше"
fi

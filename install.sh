#!/bin/sh
# Установка CehoProxy на Linux или macOS — одной командой:
#
#   curl -fsSL https://raw.githubusercontent.com/CodoCeh/CehoProxy/main/scripts/install.sh | sudo sh
#
# Локальный файл тоже подойдёт, если программа уже скачана:
#   sudo ./install.sh ./cehoproxy
#
# Дальше скрипт сам открывает настройку. Спрашивать через «curl | sh» напрямую нельзя:
# на месте клавиатуры там труба от curl, поэтому ввод берётся из /dev/tty. Если терминала
# нет вовсе (запуск из скрипта), настройка не навязывается — печатается, что делать дальше.

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

install -m 755 "$SRC" "$BIN"
mkdir -p "$ROOT"
chmod 755 "$ROOT"

# короткая команда: симлинк, а не алиас оболочки — алиас не виден скриптам и службам
ln -sf "$BIN" /usr/local/bin/chp

echo "Программа: $BIN"
echo "Короткая команда: chp"
echo "Настройки: $ROOT"
echo

if ! command -v sing-box >/dev/null 2>&1 && [ ! -x "$ROOT/sing-box" ]; then
  echo "Движок sing-box не найден — настройка предложит скачать его."
  echo "Можно поставить и самому:"
  [ "$OS" = "osx" ] && echo "  brew install sing-box" || echo "  пакетом дистрибутива"
  echo
fi

# настройка сразу после установки; клавиатура — из /dev/tty, потому что stdin занят трубой.
# Проверяем не правами на файл, а настоящим открытием: в службе и в контейнере /dev/tty
# существует, но не открывается, и скрипт падал на ровном месте
if { : < /dev/tty; } 2>/dev/null; then
  "$BIN" setup < /dev/tty
else
  echo "Терминала для вопросов нет, поэтому настройка не запущена."
  echo "Короткая команда chp уже добавлена. Дальше:"
  echo "  sudo chp setup      # язык, подписка, программы, автозапуск"
  echo "  chp                 # состояние и что делать дальше"
fi

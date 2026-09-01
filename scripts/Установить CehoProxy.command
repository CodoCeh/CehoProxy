#!/bin/bash

cd "$(dirname "$0")" || exit 1

BIN=./cehoproxy
if [ ! -x "$BIN" ]; then
  echo "Рядом с этим файлом должна лежать программа cehoproxy."
  echo "Положите оба файла в одну папку и запустите установку снова."
  echo
  read -r -p "Нажмите Enter, чтобы закрыть окно."
  exit 1
fi

xattr -d com.apple.quarantine "$BIN" 2>/dev/null

echo "Для установки нужен пароль администратора — тот же, которым вы входите в систему."
echo "Он вводится в это окно, мы его не видим и никуда не отправляем."
echo

echo "Страница продукта: https://github.com/CodoCeh/CehoProxy"
echo

sudo "$BIN" install

echo
read -r -p "Готово. Нажмите Enter, чтобы закрыть окно."

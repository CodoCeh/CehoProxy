#!/bin/sh

BIN=/usr/local/bin/cehoproxy

case "$(printf '%s' "${LANG:-ru}" | cut -c1-2)" in
  ru) T_TITLE="CehoProxy"
      T_LINK="Вставьте ссылку на подписку от вашего VPN-сервиса.\nЕё можно пропустить и добавить позже в панели."
      T_APPS="Выберите программы, которым нужен VPN. Остальные будут работать напрямую."
      T_COL="Программа"; T_PATH="Путь"
      T_DONE="Настройка завершена. Панель откроется в браузере."
      T_NOGUI="Для окна настройки нужен zenity или kdialog. Настройка в терминале: sudo chp setup"
      LANG_CODE=ru ;;
  *)  T_TITLE="CehoProxy"
      T_LINK="Paste the subscription link from your VPN service.\nYou can skip it and add it later in the panel."
      T_APPS="Choose the programs that need the VPN. The rest keep working directly."
      T_COL="Program"; T_PATH="Path"
      T_DONE="Setup finished. The panel opens in the browser."
      T_NOGUI="The setup window needs zenity or kdialog. Terminal setup: sudo chp setup"
      LANG_CODE=en ;;
esac

[ -x "$BIN" ] || exit 1

DETECTED="$("$BIN" detect-apps 2>/dev/null)"
PICKED=""

if command -v zenity >/dev/null 2>&1; then
  LINK="$(zenity --entry --title="$T_TITLE" --text="$T_LINK" --width=480 2>/dev/null)" || LINK=""
  if [ -n "$DETECTED" ]; then
    ROWS="$(printf '%s\n' "$DETECTED" | awk -F'\t' '{print ($1=="R" ? "TRUE" : "FALSE"); print $2; print $3}')"
    PICKED="$(printf '%s\n' "$ROWS" | zenity --list --checklist --title="$T_TITLE" --text="$T_APPS" \
      --column="" --column="$T_COL" --column="$T_PATH" --print-column=3 --separator='
' --width=680 --height=480 2>/dev/null)" || PICKED=""
  fi
elif command -v kdialog >/dev/null 2>&1; then
  LINK="$(kdialog --title "$T_TITLE" --inputbox "$T_LINK" 2>/dev/null)" || LINK=""
  if [ -n "$DETECTED" ]; then
    set --
    OLD_IFS="$IFS"; IFS='
'
    for ROW in $DETECTED; do
      KIND="$(printf '%s' "$ROW" | cut -f1)"; NAME="$(printf '%s' "$ROW" | cut -f2)"; APATH="$(printf '%s' "$ROW" | cut -f3)"
      STATE=off; [ "$KIND" = R ] && STATE=on
      set -- "$@" "$APATH" "$NAME" "$STATE"
    done
    IFS="$OLD_IFS"
    PICKED="$(kdialog --title "$T_TITLE" --separate-output --checklist "$T_APPS" "$@" 2>/dev/null)" || PICKED=""
  fi
else
  echo "$T_NOGUI"
  exit 1
fi

LINK="$(printf '%s' "$LINK" | tr -d '"' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"

set -- setup --lang "$LANG_CODE"
[ -n "$LINK" ] && set -- "$@" --sub "$LINK" --autostart
OLD_IFS="$IFS"; IFS='
'
for APP in $PICKED; do
  [ -n "$APP" ] && set -- "$@" --app "$APP"
done
IFS="$OLD_IFS"

if [ "$(id -u)" = "0" ]; then
  "$BIN" "$@"
else
  pkexec "$BIN" "$@" || exit 1
fi

if command -v zenity >/dev/null 2>&1; then
  zenity --info --title="$T_TITLE" --text="$T_DONE" --width=360 2>/dev/null
fi
/usr/lib/cehoproxy/open-panel.sh

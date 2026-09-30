#!/bin/sh

BIN=/usr/local/bin/cehoproxy

case "$(printf '%s' "${LANG:-ru}" | cut -c1-2)" in
  ru) T_TITLE="CehoProxy"
      T_LINK="Вставьте ссылку на подписку от вашего VPN-сервиса.\nЕё можно пропустить и добавить позже в панели."
      T_APPS="Отправить в туннель все найденные на этом компьютере программы из рекомендуемых?"
      T_DONE="Настройка завершена. Панель откроется в браузере."
      T_NOGUI="Для окна настройки нужен zenity или kdialog. Настройка в терминале: sudo chp setup"
      LANG_CODE=ru ;;
  *)  T_TITLE="CehoProxy"
      T_LINK="Paste the subscription link from your VPN service.\nYou can skip it and add it later in the panel."
      T_APPS="Send all recommended programs found on this computer through the tunnel?"
      T_DONE="Setup finished. The panel opens in the browser."
      T_NOGUI="The setup window needs zenity or kdialog. Terminal setup: sudo chp setup"
      LANG_CODE=en ;;
esac

[ -x "$BIN" ] || exit 1

if command -v zenity >/dev/null 2>&1; then
  LINK="$(zenity --entry --title="$T_TITLE" --text="$T_LINK" --width=480 2>/dev/null)" || LINK=""
  zenity --question --title="$T_TITLE" --text="$T_APPS" --width=480 2>/dev/null && ALL=1 || ALL=0
elif command -v kdialog >/dev/null 2>&1; then
  LINK="$(kdialog --title "$T_TITLE" --inputbox "$T_LINK" 2>/dev/null)" || LINK=""
  kdialog --title "$T_TITLE" --yesno "$T_APPS" 2>/dev/null && ALL=1 || ALL=0
else
  echo "$T_NOGUI"
  exit 1
fi

LINK="$(printf '%s' "$LINK" | tr -d '"' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"

set -- setup --lang "$LANG_CODE"
[ -n "$LINK" ] && set -- "$@" --sub "$LINK" --autostart
[ "$ALL" = 1 ] && set -- "$@" --all-apps

if [ "$(id -u)" = "0" ]; then
  "$BIN" "$@"
else
  pkexec "$BIN" "$@" || exit 1
fi

if command -v zenity >/dev/null 2>&1; then
  zenity --info --title="$T_TITLE" --text="$T_DONE" --width=360 2>/dev/null
fi
/usr/lib/cehoproxy/open-panel.sh

# Политика конфиденциальности CehoProxy

Действует с 1 ноября 2026 года. Относится к приложению CehoProxy для Android (`ru.cebers.cehoproxy`) и к программе CehoProxy для компьютера.

## Коротко

CehoProxy — клиент для подключения к прокси- и VPN-серверам, которые вы указываете сами. У разработчика нет своих VPN-серверов, аккаунтов и аналитики. Приложение не показывает рекламу и не передаёт разработчику никаких данных о вас.

## Что хранится на устройстве

- ссылки на подписки, список серверов и ваши настройки;
- список приложений и сайтов, которые вы направили через VPN или мимо него;
- журнал событий подключения для диагностики.

Эти данные хранятся только в памяти приложения. Резервное копирование Android для них выключено. Файл экспорта настроек создаётся только по вашей команде и сохраняется туда, куда вы укажете.

## Куда приложение обращается по сети

- **Серверы и подписки, которые добавили вы.** Через них идёт ваш трафик. Что эти серверы делают с трафиком, определяет их владелец, а не CehoProxy.
- **Проверка соединения.** Чтобы показать, работает ли сервер и в какой стране он выходит в интернет, приложение делает короткие запросы к `1.1.1.1` (Cloudflare), `www.gstatic.com` (Google), `api.country.is` и `gemini.google.com`. Эти сервисы видят только IP-адрес сервера, через который идёт запрос.
- **Проверка обновлений.** Версия с GitHub и программа для компьютера при проверке обновлений запрашивают список выпусков у `api.github.com`. Версия из Google Play обновляется через Google Play и сама GitHub не опрашивает.
- **Сканирование QR-кода** выполняет модуль Google Play Services на устройстве. Google может собирать технические данные о работе модуля по своей политике.
- **Передача подписки на другое устройство** (телевизор, магнитолу) идёт напрямую внутри вашей сети Wi-Fi или точки доступа телефона. Устройства находят друг друга по имени в локальной сети. Серверы разработчика и интернет для передачи не используются.

## Список приложений и буфер обмена

- Приложение читает список установленных приложений, чтобы вы могли выбрать, какие из них пускать через VPN. Список никуда не отправляется.
- Когда вы открываете приложение, оно проверяет буфер обмена. Если там есть ссылка на подписку, приложение предлагает её добавить. Содержимое буфера никуда не отправляется.

## Дети

Приложение не предназначено для детей младше 13 лет и сознательно не собирает данные о них.

## Удаление данных

Удалите подписки в приложении или удалите само приложение. Все данные удалятся вместе с ним. У разработчика ваших данных нет, поэтому удалять на его стороне нечего.

## Изменения и связь

Новые версии политики публикуются в этом файле. Вопросы задавайте в [GitHub Issues](https://github.com/CodoCeh/CehoProxy/issues) или в Telegram-канале [@CodoCeh](https://t.me/CodoCeh).

---

# CehoProxy Privacy Policy

Effective 1 November 2026. Applies to the CehoProxy Android app (`ru.cebers.cehoproxy`) and the CehoProxy desktop program.

## Summary

CehoProxy is a client for proxy and VPN servers that you add yourself. The developer runs no VPN servers, accounts or analytics. The app shows no ads and sends no data about you to the developer.

## Data stored on the device

- subscription links, the server list and your settings;
- the apps and sites you send through the VPN or around it;
- a connection event log for troubleshooting.

This data stays in the app's private storage. Android backup is turned off for it. A settings export file is created only when you ask for it and is saved where you choose.

## Network connections

- **Servers and subscriptions you add.** Your traffic goes through them. Their owner, not CehoProxy, decides what happens to that traffic.
- **Connection checks.** To show whether a server works and which country it exits in, the app sends short requests to `1.1.1.1` (Cloudflare), `www.gstatic.com` (Google), `api.country.is` and `gemini.google.com`. These services see only the IP address of the server the request goes through.
- **Update checks.** The GitHub build and the desktop program ask `api.github.com` for the release list when they check for updates. The Google Play build is updated by Google Play and does not contact GitHub.
- **QR code scanning** is done on the device by a Google Play Services module. Google may collect technical data about that module under its own policy.
- **Sending a subscription to another device** (a TV or car head unit) goes directly over your Wi-Fi network or the phone's hotspot. Devices find each other by name on the local network. No developer servers and no internet connection are used.

## App list and clipboard

- The app reads the list of installed apps so that you can choose which ones use the VPN. The list is not sent anywhere.
- When you open the app, it checks the clipboard. If it holds a subscription link, the app offers to add it. Clipboard contents are not sent anywhere.

## Children

The app is not meant for children under 13 and does not knowingly collect data about them.

## Deleting data

Delete the subscriptions in the app or uninstall the app. All data is removed with it. The developer holds none of your data, so nothing needs to be deleted on the developer's side.

## Changes and contact

New versions of this policy are published in this file. Send questions through [GitHub Issues](https://github.com/CodoCeh/CehoProxy/issues) or the Telegram channel [@CodoCeh](https://t.me/CodoCeh).

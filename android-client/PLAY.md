# Публикация в Google Play

## Сборка

```bash
./gradlew :app:bundlePlay
```

Файл: `app/build/outputs/bundle/play/app-play.aab`. Подпись — тот же ключ, что у APK с GitHub (`~/.android-keys/cehoproxy-release.properties`).

Отличия сборки `play` от версии с GitHub (`BuildConfig.PLAY`, `src/play/AndroidManifest.xml`):

| Что убрано | Почему |
|---|---|
| Самообновление с GitHub, `REQUEST_INSTALL_PACKAGES` | Приложениям из Google Play нельзя обновлять себя в обход магазина |
| Окно «Приложения» в уведомлении, `PACKAGE_USAGE_STATS` | Закрытое разрешение, без отдельного одобрения Google отклонит |
| «Блокировка рекламных сайтов» | Политика VpnService запрещает менять чужую рекламу через VPN |

## Ключ подписи

В Play Console при первой загрузке выбрать **«Use existing app signing key»** и загрузить наш ключ инструментом PEPK, который даёт Play Console. Тогда обновления из Google Play и с GitHub ставятся друг поверх друга. Если отдать подпись Google, пользователям придётся удалять приложение при переходе между источниками.

## Формы Play Console

- **Политика конфиденциальности:** https://github.com/CodoCeh/CehoProxy/blob/main/PRIVACY.md
- **VpnService:** основная функция — VPN-клиент для серверов пользователя. Шифрование: VLESS/Reality, Trojan, Shadowsocks, Hysteria2 и другие протоколы sing-box и Xray. Видео до 90 секунд: открыть приложение, добавить подписку, подключиться, показать значок VPN и смену IP.
- **Foreground service (`specialUse`):** VPN-туннель, включённый пользователем; уведомление видно всё время работы.
- **`REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`:** без исключения Android выгружает туннель в фоне; запрос только по кнопке «Не выгружать из памяти».
- **Безопасность данных:** данные не собираются и не передаются разработчику. Список приложений и буфер обмена обрабатываются только на устройстве.
- **Реклама:** нет. **Аудитория:** 18+. **Категория:** Инструменты.

## Аккаунт

Приложения с VpnService публикуются только с аккаунта организации (Play Console Help, «Choose a developer account type»: «Apps approved to use the VpnService class»). Личный аккаунт не подойдёт. Для организации нужен номер D-U-N-S.

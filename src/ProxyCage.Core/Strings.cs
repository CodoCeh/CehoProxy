namespace ProxyCage.Core;

/// <summary>
/// Все строки, которые видит человек. Язык выбирается при первой настройке:
/// у части серверов нет графики, и терминал обязан говорить понятно сразу.
/// Обращение — на «вы», как во всех продуктах КодоЦеха.
/// </summary>
public static class Strings
{
    public static readonly string[] Languages = { "ru", "en" };

    public static string Normalize(string? lang) =>
        lang is not null && Languages.Contains(lang.ToLowerInvariant()) ? lang.ToLowerInvariant() : "ru";

    public static string T(string? lang, string key, params object[] args)
    {
        var l = Normalize(lang);
        if (!Table.TryGetValue(key, out var pair)) return key;
        var text = l == "en" ? pair.En : pair.Ru;
        return args.Length == 0 ? text : string.Format(text, args);
    }

    private readonly record struct Pair(string Ru, string En);

    private static readonly Dictionary<string, Pair> Table = new(StringComparer.Ordinal)
    {
        // ── общее ────────────────────────────────────────────────────
        ["tagline"] = new(
            "CehoProxy — выбранные программы выходят в интернет только через туннель.",
            "CehoProxy — selected apps reach the internet only through the tunnel."),
        ["panel_at"] = new("Панель управления: {0}", "Control panel: {0}"),
        ["rules_rebuilt"] = new(
            "Правила пересобраны. Если защита включена, перезапустите её, чтобы применить.",
            "Rules rebuilt. If protection is on, restart it to apply."),
        ["cancelled"] = new("Отменено.", "Cancelled."),

        // ── состояние ────────────────────────────────────────────────
        ["state_on"] = new("Защита включена", "Protection is on"),
        ["state_off"] = new("Защита выключена", "Protection is off"),
        ["state_leftovers"] = new(
            "Служба не работает, но в системе остался туннель от прошлого запуска. Снимите его: {0}chp stop",
            "The service is not running, but a tunnel from a previous run is still in the system. Remove it: {0}chp stop"),
        ["stop_cleaned"] = new(
            "Служба не работала; следы прошлого запуска сняты.",
            "The service was not running; leftovers from the previous run were removed."),
        ["state_broken"] = new(
            "Защита не работает: служба запущена, но туннель не поднялся",
            "Protection is not working: the service runs, but the tunnel did not come up"),
        ["state_direct"] = new(
            "трафик программ идёт напрямую", "app traffic goes directly"),
        ["state_checking"] = new("проверяю выход", "checking the exit"),
        ["state_no_exit"] = new(
            "ноды не отвечают, соединения программ рвутся",
            "no node responds, app connections are dropped"),
        ["exit_is"] = new("выход: {0} · {1}", "exit: {0} · {1}"),
        ["apps_isolated"] = new("Программ в изоляции: {0}", "Apps isolated: {0}"),
        ["subs_count"] = new("Подписок: {0}", "Subscriptions: {0}"),
        ["country_any"] = new("любая", "any"),

        // ── предстартовая проверка ───────────────────────────────────
        ["pf_rights_ok"] = new("Прав достаточно", "Sufficient privileges"),
        ["pf_rights_need_win"] = new("Нужны права администратора", "Administrator rights required"),
        ["pf_rights_need_unix"] = new("Нужен запуск от root", "Must be run as root"),
        ["pf_rights_detail"] = new(
            "Без этого нельзя создать сетевой интерфейс, через который идёт изоляция.",
            "Without them the network interface used for isolation cannot be created."),
        ["pf_rights_fix_win"] = new(
            "Закройте программу, нажмите на её значок правой кнопкой и выберите «Запуск от имени администратора». " +
            "Чтобы не делать это каждый раз, включите автозапуск — он стартует с нужными правами сам.",
            "Close the app, right-click its icon and choose \"Run as administrator\". " +
            "To avoid doing this every time, enable autostart — it starts with the right privileges by itself."),
        ["pf_rights_fix_unix"] = new(
            "Запустите через sudo: sudo chp daemon. Чтобы не вводить пароль каждый раз, включите автозапуск: sudo chp autostart on.",
            "Run it with sudo: sudo chp daemon. To avoid typing the password every time, enable autostart: sudo chp autostart on."),
        ["pf_engine_ok"] = new("Движок на месте", "Engine found"),
        ["pf_engine_missing"] = new("Не найден {0}", "{0} not found"),
        ["pf_engine_detail"] = new(
            "Искали в {0}, рядом с программой и в PATH.",
            "Looked in {0}, next to the program and in PATH."),
        ["pf_engine_fix_win"] = new(
            "Положите {0} рядом с программой. Он идёт в комплекте поставки.",
            "Put {0} next to the program. It ships with the package."),
        ["pf_engine_fix_unix"] = new(
            "Поставьте sing-box пакетным менеджером системы или положите файл в {0}.",
            "Install sing-box with your package manager or put the file into {0}."),
        ["pf_curl_ok"] = new("Проверка выхода доступна", "Exit check available"),
        ["pf_curl_missing"] = new("Не найден curl", "curl not found"),
        ["pf_curl_detail"] = new(
            "Через него проверяется реальный IP выхода и живость подписки.",
            "It is used to check the real exit IP and whether the subscription works."),
        ["pf_curl_fix_win"] = new(
            "Он есть в Windows 10 сборки 1803 и новее. Изоляция будет работать, но реальный IP выхода показан не будет.",
            "It ships with Windows 10 build 1803 and newer. Isolation will work, but the real exit IP will not be shown."),
        ["pf_curl_fix_unix"] = new(
            "Поставьте curl пакетным менеджером. Изоляция будет работать, но реальный IP выхода показан не будет и автопереключение нод работать не сможет.",
            "Install curl with your package manager. Isolation will work, but the real exit IP will not be shown and automatic node switching will not work."),
        ["pf_tun_missing"] = new("В системе нет /dev/net/tun", "/dev/net/tun is missing"),
        ["pf_tun_detail"] = new(
            "Без этого устройства туннель не поднимется.",
            "The tunnel cannot start without this device."),
        ["pf_tun_fix"] = new(
            "Загрузите модуль: sudo modprobe tun. Чтобы он подхватывался сам, " +
            "добавьте строку «tun» в /etc/modules-load.d/tun.conf.",
            "Load the module: sudo modprobe tun. To load it automatically, " +
            "add the line \"tun\" to /etc/modules-load.d/tun.conf."),
        ["pf_ip_missing"] = new("В системе нет команды ip", "The ip command is missing"),
        ["pf_ip_detail"] = new(
            "Ею снимаются следы прошлого запуска; без неё сеть может остаться сломанной после сбоя.",
            "It removes leftovers from the previous run; without it the network may stay broken after a crash."),
        ["pf_ip_fix"] = new(
            "Поставьте пакет iproute2: sudo apt install iproute2 (или аналог вашего дистрибутива).",
            "Install iproute2: sudo apt install iproute2 (or your distribution's equivalent)."),
        ["pf_dir_ok"] = new("Папка настроек доступна на запись", "Settings folder is writable"),
        ["pf_dir_bad"] = new("Не удаётся писать в папку настроек", "Cannot write to the settings folder"),
        ["pf_dir_fix_win"] = new(
            "Запустите от имени администратора либо укажите другую папку переменной CEHOPROXY_HOME.",
            "Run as administrator or point CEHOPROXY_HOME to another folder."),
        ["pf_dir_fix_unix"] = new(
            "Запустите через sudo либо укажите другую папку переменной CEHOPROXY_HOME.",
            "Run with sudo or point CEHOPROXY_HOME to another folder."),
        ["pf_port_ok"] = new("Порт панели {0} свободен", "Panel port {0} is free"),
        ["pf_port_busy"] = new("Порт {0} уже занят", "Port {0} is already taken"),
        ["pf_port_detail"] = new(
            "На нём открывается панель управления.", "The control panel is served on it."),
        ["pf_port_fix"] = new(
            "Выберите свободный порт: chp set-port {0}",
            "Pick a free port: chp set-port {0}"),
        ["pf_port_unknown"] = new("Не удалось проверить порт {0}", "Could not check port {0}"),
        ["pf_no_subs"] = new("Не добавлено ни одной подписки", "No subscriptions added"),
        ["pf_no_subs_detail"] = new(
            "Программа поставляется без подписок: ссылку выдаёт ваш VPN-сервис.",
            "The program ships without subscriptions: your VPN service provides the link."),
        ["pf_no_subs_fix"] = new(
            "Добавьте ссылку в разделе «Подписки».", "Add the link in the \"Subscriptions\" section."),
        ["pf_no_apps"] = new("Не выбрано ни одной программы", "No apps selected"),
        ["pf_no_apps_detail"] = new(
            "Изолировать нечего, защиту включать не от чего.",
            "There is nothing to isolate, so there is nothing to protect."),
        ["pf_no_apps_fix"] = new(
            "Добавьте программу в разделе «Программы в изоляции».",
            "Add an app in the \"Isolated apps\" section."),
        ["pf_all_ready"] = new(
            "Всё готово, можно включать защиту.", "Everything is ready, protection can be turned on."),
        ["pf_blockers"] = new(
            "Мешает запуску: {0}. Исправьте отмеченное «стоп».",
            "Blocking startup: {0}. Fix the items marked \"stop\"."),

        // ── определение программ ─────────────────────────────────────
        ["det_bundle"] = new(
            "Под изоляцию попадёт весь пакет программы целиком, включая вспомогательные процессы внутри него.",
            "The whole application bundle will be isolated, including helper processes inside it."),
        ["det_folder"] = new(
            "Под изоляцию попадут все процессы, запущенные из этой папки и вложенных в неё.",
            "Every process started from this folder and its subfolders will be isolated."),
        ["det_climbed"] = new(
            " Поднялись из служебной подпапки к корню программы.",
            " Moved up from a service subfolder to the application root."),
        ["det_resolved"] = new(
            " Путь показан так, как его видит система: по дороге были символические ссылки, " +
            "а правило должно совпадать именно с системным путём.",
            " The path is shown as the system sees it: there were symbolic links along the way, " +
            "and the rule must match the system path exactly."),
        ["det_system_dir"] = new(
            "Программа лежит в системном каталоге ({0}), изолировать его целиком нельзя. " +
            "Правило построено по одному этому файлу. Если программа запускает вспомогательные " +
            "процессы из других файлов, добавьте и их.",
            "The program is in a system directory ({0}), which cannot be isolated as a whole. " +
            "The rule covers this single file. If the program starts helper processes from other " +
            "files, add those too."),
        ["det_refuse_system_dir"] = new(
            "{0} — системный каталог, изолировать его целиком нельзя: под правило уехала бы " +
            "половина системы. Укажите путь к самой программе, а не к каталогу.",
            "{0} is a system directory and cannot be isolated as a whole: half the system would " +
            "fall under the rule. Point to the program itself, not to the directory."),
        ["det_msix"] = new(
            "Программа из Microsoft Store. В пути есть номер версии, он меняется при обновлении, " +
            "поэтому правило строится по «{0}_» без версии — иначе после обновления программа " +
            "молча вышла бы из-под изоляции.",
            "A Microsoft Store app. The path contains a version number that changes on update, so " +
            "the rule uses \"{0}_\" without the version — otherwise the app would silently fall out " +
            "of isolation after an update."),

        // ── пароль и доступ ──────────────────────────────────────────
        ["auth_password"] = new("Пароль", "Password"),
        ["auth_enter"] = new("Войти", "Sign in"),
        ["auth_wrong"] = new("Неверный пароль.", "Wrong password."),
        ["auth_hint"] = new(
            "Панель доступна всем, кто вошёл на этот компьютер, поэтому она закрыта паролем.",
            "The panel is reachable by anyone logged into this machine, so it is password-protected."),
        ["auth_needed_cli"] = new(
            "Нужен пароль. Укажите его ключом --password или переменной CEHOPROXY_PASSWORD.",
            "A password is required. Pass it with --password or via CEHOPROXY_PASSWORD."),
        ["auth_set_ok"] = new("Пароль установлен.", "Password set."),
        ["auth_cleared"] = new("Пароль снят.", "Password removed."),
        ["auth_no_password"] = new(
            "Пароль не задан: панелью и командами может пользоваться любой, кто вошёл на эту машину.",
            "No password set: anyone logged into this machine can use the panel and the commands."),

        // ── страны ───────────────────────────────────────────────────
        ["countries_title"] = new("Страны выхода", "Exit countries"),
        ["countries_hint"] = new(
            "Снятая галочка означает, что ноды этой страны в пул не берутся. " +
            "Если под фильтр не попадёт ни одна нода, туннель не поднимется.",
            "An unchecked country means its nodes are excluded from the pool. " +
            "If no node passes the filter, the tunnel will not start."),
        ["countries_none_left"] = new(
            "После фильтра по странам не осталось ни одной ноды: {0}. " +
            "Верните хотя бы одну страну.",
            "No nodes left after the country filter: {0}. Turn at least one country back on."),
        ["col_country"] = new("Страна", "Country"),
        ["col_nodes"] = new("Нод", "Nodes"),
        ["col_alive"] = new("Отвечают", "Responding"),
        ["col_best"] = new("Лучшая задержка", "Best latency"),
        ["col_protocols"] = new("Протоколы", "Protocols"),
        ["col_use"] = new("Использовать", "Use"),
        ["not_measured"] = new("не измерено", "not measured"),
        ["measure_blocked"] = new(
            "Пока защита включена, задержки измерить нельзя: туннель отвечает за все адреса сам, " +
            "и любая нода выглядит живой. Выключите защиту, замерьте, потом включите обратно.",
            "Latency cannot be measured while protection is on: the tunnel answers for every address " +
            "itself, so every node looks alive. Turn protection off, measure, then turn it back on."),
        ["measure_done"] = new("Замерено стран: {0}.", "Countries measured: {0}."),
        ["speed_title"] = new("Отсев медленных нод", "Dropping slow nodes"),
        ["speed_row"] = new("Медленные ноды", "Slow nodes"),
        ["speed_row_off"] = new("не отсеиваются", "kept"),
        ["speed_ask"] = new(
            "Отсеять медленные ноды? Замер займёт несколько секунд",
            "Drop slow nodes? Measuring takes a few seconds"),
        ["speed_limit"] = new(
            "Порог задержки, мс (выше — нода в пул не идёт)",
            "Latency threshold, ms (slower nodes stay out of the pool)"),
        ["speed_measuring"] = new("Меряю задержки…", "Measuring latency…"),
        ["speed_result"] = new(
            "Замерено нод: {0}. Отсеяно как медленные: {1}. В пуле останется: {2}.",
            "Nodes measured: {0}. Dropped as slow: {1}. Remaining in the pool: {2}."),
        ["speed_local_accept"] = new(
            "Замер не годится: ноды из разных стран ответили одинаково быстро, а так не бывает — " +
            "до Хельсинки и до Москвы не может быть по пять миллисекунд. Значит, соединения принимает " +
            "туннель, уже поднятый на этой машине (наш или чужой VPN), и числа не про скорость нод. " +
            "Выключите посторонний VPN и повторите: chp speed <мс>",
            "The measurement is unusable: nodes in different countries answered equally fast, which cannot " +
            "happen — Helsinki and Moscow cannot both be five milliseconds away. Connections are being accepted " +
            "by a tunnel already running on this machine (ours or another VPN), so the numbers say nothing about " +
            "node speed. Turn the other VPN off and repeat: chp speed <ms>"),
        ["speed_off"] = new("Отсев по скорости выключен.", "Slow-node filtering is off."),
        ["speed_state_on"] = new("не медленнее {0} мс", "no slower than {0} ms"),
        ["speed_none_left"] = new(
            "После отсева медленнее {0} мс в пуле не осталось ни одной ноды. Поднимите порог или выключите отсев: chp speed off",
            "No node is left in the pool after dropping everything slower than {0} ms. Raise the threshold or turn the filter off: chp speed off"),
        ["speed_unmeasured_note"] = new(
            "Ноды hysteria2 и tuic по скорости не отсеиваются: они поверх UDP, и TCP-замер соврал бы.",
            "hysteria2 and tuic nodes are never dropped by speed: they run over UDP and a TCP probe would lie."),
        ["speed_usage"] = new(
            "Нужно: chp speed <мс> · chp speed off",
            "Usage: chp speed <ms> · chp speed off"),
        ["udp_not_measured"] = new(
            "«Не измерено» у hysteria2 и tuic — это норма: они работают поверх UDP, " +
            "а замер идёт TCP-рукопожатием, и он бы соврал.",
            "\"Not measured\" for hysteria2 and tuic is expected: they run over UDP, while the probe " +
            "is a TCP handshake, which would lie."),

        // ── панель: разделы ──────────────────────────────────────────
        ["nav_state"] = new("Состояние", "Status"),
        ["nav_apps"] = new("Программы", "Apps"),
        ["nav_subs"] = new("Подписки", "Subscriptions"),
        ["nav_exit"] = new("Страны", "Countries"),
        ["nav_browser"] = new("Браузер", "Browser"),
        ["nav_access"] = new("Доступ", "Access"),
        ["nav_help"] = new("Помощь", "Help"),
        ["apps_title"] = new("Программы в изоляции", "Isolated apps"),
        ["apps_lede"] = new(
            "Указанные программы выходят в интернет только через туннель. Если ни одна нода " +
            "не отвечает, их соединения рвутся, а не уходят напрямую. Остальная система работает как обычно.",
            "The listed apps reach the internet only through the tunnel. If no node responds, their " +
            "connections are dropped instead of going out directly. The rest of the system works as usual."),
        ["apps_empty"] = new(
            "Пока ничего не добавлено. Укажите путь к программе ниже.",
            "Nothing added yet. Enter a path to a program below."),
        ["apps_placeholder_win"] = new(
            "C:\\Program Files\\Программа\\program.exe", "C:\\Program Files\\App\\program.exe"),
        ["apps_placeholder_mac"] = new("/Applications/Программа.app", "/Applications/App.app"),
        ["apps_placeholder_linux"] = new("/opt/программа/program", "/opt/app/program"),
        ["apps_hint"] = new(
            "Можно указать и сам файл программы, и её папку. Папку определим сами и покажем, " +
            "что попадёт под правило: изолируется вся папка целиком, потому что программы " +
            "часто запускают вспомогательные процессы с другими именами.",
            "You can point either to the program file or to its folder. We detect the folder and show " +
            "what the rule will cover: the whole folder is isolated, because programs often start " +
            "helper processes under different names."),
        ["apps_hint_mac"] = new(
            "Для программы из /Applications укажите сам пакет .app.",
            "For an app from /Applications point to the .app bundle itself."),
        ["apps_hint_sysdir"] = new(
            "Программу из системного каталога изолируем по одному файлу: весь каталог трогать нельзя.",
            "A program from a system directory is isolated as a single file: the whole directory must not be touched."),
        ["subs_empty"] = new(
            "Нет ни одной подписки. Добавьте ссылку, которую вам выдал VPN-сервис.",
            "No subscriptions yet. Add the link your VPN service gave you."),
        ["subs_pool"] = new(
            "Ноды всех подписок складываются в общий пул, и туннель сам выбирает живую. " +
            "Переключать подписку целиком не нужно.",
            "Nodes from all subscriptions go into one pool and the tunnel picks a working one itself. " +
            "There is no need to switch the whole subscription."),
        ["col_name"] = new("Имя", "Name"),
        ["col_link"] = new("Ссылка", "Link"),
        ["col_folder"] = new("Папка", "Folder"),
        ["col_state"] = new("Состояние", "State"),
        ["btn_add"] = new("Добавить", "Add"),
        ["btn_remove"] = new("Убрать", "Remove"),
        ["btn_delete"] = new("Удалить", "Delete"),
        ["btn_save"] = new("Сохранить", "Save"),
        ["btn_on"] = new("Включить", "Turn on"),
        ["btn_off"] = new("Выключить", "Turn off"),
        ["btn_measure"] = new("Замерить задержки", "Measure latency"),
        ["autostart_title"] = new("Автозапуск", "Autostart"),
        ["autostart_on"] = new("Включается при старте системы", "Starts with the system"),
        ["autostart_off"] = new("Запускается только вручную", "Started manually only"),
        ["autostart_add"] = new("Добавить в автозапуск", "Enable autostart"),
        ["autostart_del"] = new("Убрать из автозапуска", "Disable autostart"),

        // ── браузер ──────────────────────────────────────────────────
        ["browser_title"] = new("Прокси для браузера", "Proxy for the browser"),
        ["browser_lede"] = new(
            "Кроме изоляции по программам туннель отдаёт обычный прокси на этом компьютере. " +
            "Пригодится, когда нужно завернуть только один браузер или профиль, не трогая систему.",
            "Besides per-app isolation the tunnel also serves a plain proxy on this machine. " +
            "Useful when you need to route just one browser or profile without touching the system."),
        ["browser_howto"] = new(
            "В Firefox: Настройки → Сеть → Настроить → Ручная настройка → SOCKS5, узел 127.0.0.1, порт {0}, " +
            "и включите «Проксировать DNS при использовании SOCKS 5». " +
            "В Chrome удобнее расширением для прокси или ярлыком с ключом --proxy-server=socks5://127.0.0.1:{0}",
            "Firefox: Settings → Network → Settings → Manual proxy → SOCKS5, host 127.0.0.1, port {0}, " +
            "and enable \"Proxy DNS when using SOCKS v5\". " +
            "In Chrome use a proxy extension or a shortcut with --proxy-server=socks5://127.0.0.1:{0}"),
        ["browser_note"] = new(
            "Прокси работает, только пока защита включена.",
            "The proxy works only while protection is on."),

        ["rotation_label"] = new(
            "Переключаться на живую ноду автоматически",
            "Switch to a working node automatically"),
        ["checkurl_hint"] = new(
            "По этому адресу проверяется, работает ли туннель, и по нему же принимается решение " +
            "о переключении. Укажите то, ради чего туннель вам нужен: нода может отвечать и при этом " +
            "не пускать на нужный сайт, поэтому проверка идёт настоящим запросом, а не пингом.",
            "This address is used to check whether the tunnel works and to decide about switching. " +
            "Point it at what you actually need: a node can respond and still not let you reach the site, " +
            "so the check is a real request, not a ping."),
        ["remote_no_panel"] = new(
            "Настройки открыты только администратору, а панель сейчас не запущена. Попросите администратора запустить службу: sudo chp daemon",
            "Settings are readable by the administrator only, and the panel is not running. Ask the administrator to start the service: sudo chp daemon"),
        ["remote_failed"] = new(
            "Не удалось обратиться к панели: {0}", "Could not reach the panel: {0}"),
        ["remote_not_allowed"] = new(
            "Команду «{0}» может выполнить только администратор на этой машине.",
            "The \"{0}\" command can only be run by an administrator on this machine."),
        ["pool_empty"] = new(
            "Ни одна подписка не отдала нод, и рабочих копий на диске нет. Проверьте ссылки в разделе «Подписки».",
            "No subscription returned any node and there are no working copies on disk. Check the links in \"Subscriptions\"."),
        ["ai_title"] = new("Найдено на этом компьютере", "Found on this machine"),
        ["ai_lede"] = new(
            "Известные ИИ-инструменты найдены в обычных местах установки. Отправьте в туннель те, которым он нужен: остальные продолжат работать напрямую.",
            "Known AI tools were found in their usual install locations. Send the ones that need the tunnel through it; the rest keep working directly."),
        ["ai_none"] = new(
            "Ничего знакомого не нашлось. Программу можно добавить вручную, указав путь ниже.",
            "Nothing familiar was found. You can add a program manually by entering its path below."),
        ["ai_add"] = new("В туннель", "Send through tunnel"),
        ["ai_added"] = new("уже в туннеле", "already tunneled"),
        ["ai_script_warn"] = new(
            "Этот помощник запускает {0} — компьютер видит её, а не его, и правило по папке не сработает. Такую команду переводят на туннель отдельно: chp wrap.",
            "This helper is started by {0} — the computer sees that program, not the helper, so a folder rule will not match. Route such a command separately: chp wrap."),
        ["ai_kind_bundle"] = new("пакет программы", "application bundle"),
        ["ai_kind_native"] = new("исполняемый файл", "executable"),
        ["ai_kind_script"] = new("скрипт", "script"),
        ["upd_title"] = new("Обновление", "Update"),
        ["upd_current"] = new("Установлена версия {0}", "Installed version {0}"),
        ["upd_check"] = new("Проверить обновления", "Check for updates"),
        ["upd_none"] = new("Обновлений нет, у вас последняя версия.", "No updates, you have the latest version."),
        ["upd_found"] = new("Доступна версия {0}.", "Version {0} is available."),
        ["upd_apply"] = new("Обновить и перезапустить", "Update and restart"),
        ["upd_done"] = new(
            "Обновлено до {0}. Защита перезапущена.", "Updated to {0}. Protection restarted."),
        ["upd_check_failed"] = new(
            "Не удалось проверить обновления: {0}", "Could not check for updates: {0}"),
        ["upd_failed"] = new("Обновиться не удалось: {0}", "Update failed: {0}"),

        ["run_title"] = new(
            "Помощники, которые запускаются командой",
            "Helpers you start with a command"),
        ["run_lede"] = new(
            "Часть помощников запускается не значком, а командой в терминале, и своей папки на диске у них нет. Такая команда — файл, который выполняет чужая программа-исполнитель, одна на десятки разных команд. Компьютер видит её, а не вашего помощника, и отправить её в туннель нельзя: туда уедут все команды подряд. Для таких есть отдельная кнопка:",
            "Some helpers are started by a command in a terminal rather than by an icon, and have no folder of their own. Such a command is a file executed by a shared runner program that serves dozens of different commands. The computer sees the runner, not your helper, so sending it through the tunnel is not an option: every command would go with it. There is a separate button for these:"),
        ["run_once"] = new("Разово", "Once"),
        ["run_always"] = new("Навсегда", "Permanently"),
        ["run_undo"] = new("Вернуть обратно", "Undo"),
        ["run_note"] = new(
            "Дальше вызывайте команду как обычно — запоминать ничего не надо. Остальные команды, даже те, что запускает та же самая программа-исполнитель, туннель не затронет.",
            "After that use the command as usual — nothing to remember. Other commands, even those started by the same runner program, are left alone."),
        ["run_need_cmd"] = new("Нужна команда: chp run <команда>", "A command is required: chp run <command>"),
        ["run_not_on"] = new(
            "Защита выключена, а команда переведена на туннель. Включите защиту: {0}chp daemon — либо верните команду на прямой выход: chp unwrap ИМЯ",
            "Protection is off while the command is routed through the tunnel. Turn protection on: {0}chp daemon, or send the command back to the direct route: chp unwrap NAME"),

        ["alias_made"] = new(
            "Короткая команда chp создана: {0}", "The short command chp is ready: {0}"),
        ["alias_failed"] = new(
            "Не удалось создать короткую команду: {0}", "Could not create the short command: {0}"),
        ["wrap_done"] = new(
            "Команда «{0}» переведена на туннель. Вызывайте её как обычно.",
            "The \"{0}\" command now goes through the tunnel. Use it as usual."),
        ["wrap_failed"] = new("Не удалось перевести на туннель: {0}", "Could not route it through the tunnel: {0}"),
        ["unwrap_done"] = new("Команда «{0}» снова ходит напрямую.", "The \"{0}\" command goes directly again."),
        ["wrap_list"] = new("Переведены на туннель", "Routed through the tunnel"),
        ["wrap_empty"] = new("Пока ни одна команда не переведена.", "No command has been routed yet."),
        ["wrap_need_name"] = new(
            "Нужно имя команды, например: chp wrap имя-команды",
            "A command name is required, for example: chp wrap command-name"),
        ["wrap_hint"] = new(
            "Вызывайте её как обычно: она сама пойдёт через туннель, а остальные команды и программы — напрямую. Вернуть обратно: chp unwrap {0}",
            "Use it as usual: it will go through the tunnel by itself, while other commands and programs stay direct. To undo: chp unwrap {0}"),
        ["sub_check"] = new("Проверить", "Check"),
        ["sub_checking"] = new(
            "Проверка идёт настоящим запросом через туннель и занимает несколько секунд.",
            "The check is a real request through the tunnel and takes a few seconds."),
        ["sub_check_off"] = new(
            "Проверить можно только при включённой защите: запрос идёт через туннель.",
            "Checking is possible only while protection is on: the request goes through the tunnel."),
        ["sub_checked_nodes"] = new(
            "Подписки перечитаны, нод в пуле: {0}.", "Subscriptions reloaded, nodes in the pool: {0}."),
        ["sub_checked_at"] = new("проверена {0}", "checked {0}"),
        ["inst_title"] = new("Установка CehoProxy", "Installing CehoProxy"),
        ["inst_need_rights"] = new(
            "Установка меняет общие для машины настройки, поэтому нужны права администратора. {0}",
            "Installation changes machine-wide settings, so administrator rights are required. {0}"),
        ["inst_engine_ask"] = new(
            "Скачать движок sing-box с сайта автора?", "Download the sing-box engine from its author's site?"),
        ["inst_engine_later"] = new(
            "Движок sing-box ещё не установлен — настройка предложит скачать его.",
            "The sing-box engine is not installed yet — the setup will offer to download it."),
        ["inst_engine_skip"] = new(
            "Хорошо. Поставьте sing-box сами, иначе туннель не поднимется.",
            "All right. Install sing-box yourself, otherwise the tunnel will not start."),
        ["inst_engine_failed"] = new(
            "Движок скачать не удалось: {0}", "Could not download the engine: {0}"),
        ["inst_done"] = new(
            "Установлено. Дальше — настройка.", "Installed. Next comes the setup."),
        ["inst_removed"] = new(
            "Программа удалена из системы.", "The program has been removed from the system."),
        ["uninstall_full"] = new(
            "Удалить и сами файлы программы?", "Remove the program files as well?"),
        ["change_reverted"] = new(
            "Изменение отменено, прежние настройки на месте.",
            "The change was reverted, the previous settings are intact."),
        ["btn_save_check"] = new(
            "Сохранить адрес проверки",
            "Save the check address"),
        ["summary_title"] = new("Кратко", "At a glance"),
        ["auth_is_set"] = new("Пароль установлен", "Password is set"),
        ["auth_not_set"] = new("Пароль не установлен", "No password set"),
        ["auth_remove"] = new("Снять пароль", "Remove password"),
        ["lang_title"] = new("Язык", "Language"),
        ["help_multiuser"] = new(
            "На сервере с несколькими пользователями достаточно одной установки: туннель один на " +
            "машину, а правило смотрит на путь программы, поэтому оно действует для всех, кто её запускает.",
            "On a multi-user server one installation is enough: there is a single tunnel per machine, " +
            "and the rule matches the program path, so it applies to everyone who runs it."),

        // ── помощь ───────────────────────────────────────────────────
        ["help_title"] = new("Как пользоваться", "How to use"),
        ["help_1"] = new(
            "Добавьте подписку VPN-сервиса.", "Add your VPN service subscription."),
        ["help_2_win"] = new(
            "Добавьте программу, которой нужен туннель. Путь можно скопировать из свойств ярлыка.",
            "Add the program that needs the tunnel. You can copy the path from the shortcut properties."),
        ["help_2_mac"] = new(
            "Добавьте программу, которой нужен туннель: перетащите её из Finder или впишите путь вида /Applications/Имя.app.",
            "Add the program that needs the tunnel: drag it from Finder or type a path like /Applications/Name.app."),
        ["help_2_linux"] = new(
            "Добавьте программу, которой нужен туннель. Путь к запускаемому файлу покажет команда which.",
            "Add the program that needs the tunnel. The which command shows the path to its executable."),
        ["help_3"] = new(
            "Нажмите «Включить». Программу после этого перезапустите, чтобы её соединения пошли по новым правилам.",
            "Press \"Turn on\". Restart the program afterwards so its connections follow the new rules."),
        ["help_4"] = new(
            "Проверьте строку состояния наверху: там показан реальный IP и страна выхода.",
            "Check the status line at the top: it shows the real exit IP and country."),
        ["help_cli"] = new(
            "То же самое из терминала: {0}chp add-app ПУТЬ, {0}chp verify, {0}chp status.",
            "The same from a terminal: {0}chp add-app PATH, {0}chp verify, {0}chp status."),
        ["footer_local"] = new(
            "панель доступна только с этого компьютера", "the panel is reachable only from this machine"),
        ["forged"] = new("Выковано в КодоЦех", "Forged at CodoCeh"),
        ["product_page"] = new("Страница продукта", "Product page"),
        ["telegram"] = new("Мы в Telegram", "We are on Telegram"),
        ["product_page_at"] = new("Страница продукта: {0}", "Product page: {0}"),

        // ── сообщения команд ─────────────────────────────────────────
        ["empty"] = new("пусто", "empty"),
        ["removed"] = new("Убрано.", "Removed."),
        ["added_name"] = new("Добавлено: {0}", "Added: {0}"),
        ["col_file"] = new("Файл", "File"),
        ["stop_sent"] = new("Сигнал остановки послан.", "Stop signal sent."),
        ["stopped"] = new("Остановлено.", "Stopped."),
        ["already_on"] = new("Защита уже включена.", "Protection is already on."),
        ["already_off"] = new(
            "Защита уже выключена.",
            "Protection is already off."),
        ["engine_died"] = new(
            "Движок завершился сразу после запуска.", "The engine exited right after start."),
        ["start_failed"] = new("Защита не включилась", "Protection did not start"),
        ["startup_blockers"] = new("Что мешает запуску:", "What blocks the start:"),
        ["panel_only"] = new(
            "Защита не включена; панель открыта: http://127.0.0.1:{0}",
            "Protection is off; the panel is open at http://127.0.0.1:{0}"),
        ["panel_not_running"] = new(
            "Панель сейчас не запущена. Сначала: {0}chp daemon",
            "The panel is not running. First: {0}chp daemon"),
        ["lang_set"] = new("Язык интерфейса: {0}", "Interface language: {0}"),
        ["sub_added"] = new("Подписка «{0}» добавлена.", "Subscription \"{0}\" added."),
        ["sub_removed"] = new("Подписка «{0}» удалена.", "Subscription \"{0}\" removed."),
        ["sub_ok"] = new("работает", "works"),
        ["sub_bad"] = new("не отвечает", "no response"),
        ["sub_unchecked"] = new("не проверялась", "not checked"),
        ["country_on"] = new("Страна {0} снова в пуле.", "Country {0} is back in the pool."),
        ["country_off"] = new("Страна {0} исключена из пула.", "Country {0} is excluded from the pool."),
        ["country_only"] = new("В пуле останутся только ноды {0}.", "The pool will keep only {0} nodes."),
        ["country_all_allowed"] = new(
            "Ограничение по одной стране снято.", "The single-country restriction is lifted."),
        ["uninstall_warn"] = new(
            "Будут сняты автозапуск и защита, удалены настройки, подписки и правила. Папка:",
            "Autostart and protection will be removed, along with settings, subscriptions and rules. Folder:"),
        ["uninstall_confirm"] = new(
            "Повторите с --yes, если согласны.", "Repeat with --yes if you agree."),
        ["uninstall_done"] = new(
            "Удалено: автозапуск, защита, настройки, подписки, правила, движок и короткая команда. Остался только файл самой программы — удалите его, когда он больше не нужен.",
            "Removed: autostart, protection, settings, subscriptions, rules, the engine and the short command. Only the program file itself remains; delete it when you no longer need it."),
        ["no_write_access"] = new(
            "Нет прав на изменение настроек ({0}). Запустите команду через {1}chp либо " +
            "воспользуйтесь панелью: она выполнит её от имени службы.",
            "No permission to change the settings ({0}). Run the command with {1}chp, or use the " +
            "panel: it will run the command on behalf of the service."),
        ["err_need_path"] = new("Нужен путь к программе.", "A path to the program is required."),
        ["err_no_such_path"] = new(
            "Такого пути нет: {0}",
            "No such path: {0}"),
        ["err_already_added"] = new("Это уже добавлено.", "Already added."),
        ["err_not_in_list"] = new("Такого в списке нет.", "Not in the list."),
        ["err_need_sub_args"] = new(
            "Нужно: sub-add <имя> <ссылка>", "Usage: sub-add <name> <link>"),
        ["err_need_sub_name"] = new("Нужно имя подписки.", "A subscription name is required."),
        ["err_sub_exists"] = new(
            "Подписка с именем «{0}» уже есть.", "A subscription named \"{0}\" already exists."),
        ["err_no_such_sub"] = new("Нет подписки «{0}».", "No subscription named \"{0}\"."),
        ["err_need_port"] = new("Нужен номер порта 1-65535.", "A port number 1-65535 is required."),
        ["err_country_usage"] = new(
            "Нужно: country on|off <КОД> · country only <КОД> · country any",
            "Usage: country on|off <CC> · country only <CC> · country any"),
        ["err_unknown_command"] = new(
            "Неизвестная команда: {0}. Запустите chp без аргументов, чтобы увидеть список.",
            "Unknown command: {0}. Run chp with no arguments to see the list."),

        // ── разговор в терминале ─────────────────────────────────────
        ["country_unknown"] = new("страна не определена", "country unknown"),
        ["ask_skip"] = new("Enter — пропустить", "Enter to skip"),
        ["ask_or_path"] = new(
            "номера через запятую или путь к программе",
            "numbers separated by commas, or a path to a program"),
        ["ask_choose"] = new("Что выбираете", "Your choice"),
        ["ask_bad_choice"] = new("Так не пойму. Нужен номер из списка.", "I do not follow. A number from the list is needed."),
        ["ask_sub_link"] = new(
            "Ссылка на подписку", "Subscription link"),
        ["ask_sub_where"] = new(
            "Ссылку выдаёт ваш VPN-сервис. Без неё туннелю некуда подключаться.",
            "Your VPN service gives you the link. Without it the tunnel has nowhere to connect."),
        ["ask_sub_checking"] = new("Проверяю ссылку…", "Checking the link…"),
        ["ask_sub_nodes"] = new("Нод получено: {0}", "Nodes received: {0}"),
        ["ask_sub_empty"] = new(
            "По этой ссылке нод нет. Проверьте её в браузере: должен открыться список или блок base64.",
            "This link returns no nodes. Open it in a browser: you should see a list or a base64 blob."),
        ["diag_server_down"] = new(
            "Сервер подписки ответил {0} — это сбой на его стороне, ссылка ни при чём. Попробуйте позже или возьмите другую ссылку у VPN-сервиса.",
            "The subscription server answered {0} — that is a failure on its side, not a problem with your link. Try later or ask your VPN service for another link."),
        ["diag_http_error"] = new(
            "Сервер подписки ответил {0}. Обычно так бывает, когда ссылка устарела или в ней опечатка.",
            "The subscription server answered {0}. That usually means the link has expired or has a typo."),
        ["diag_empty"] = new(
            "Ссылка открывается, но ответ пустой. Похоже, подписка отключена на стороне сервиса.",
            "The link opens but the answer is empty. The subscription looks disabled on the service side."),
        ["diag_html"] = new(
            "По ссылке пришла веб-страница, а не подписка ({0} знаков). Обычно это страница входа или ошибки: откройте ссылку в браузере и посмотрите, что там.",
            "The link returned a web page, not a subscription ({0} characters). Usually that is a login or error page: open it in a browser and see."),
        ["diag_unknown_format"] = new(
            "Ответ получен ({0} знаков), но ноды в нём не распознались. Пришлите этот ответ нам — разберём формат.",
            "The answer arrived ({0} characters), but no nodes were recognised in it. Send it to us and we will add the format."),
        ["diag_timeout"] = new(
            "Сервер подписки не ответил за 20 секунд. Либо он лежит, либо до него не доходит с этого компьютера.",
            "The subscription server did not answer within 20 seconds. Either it is down or it is unreachable from this computer."),
        ["diag_no_answer"] = new(
            "До сервера подписки не достучались: {0}",
            "Could not reach the subscription server: {0}"),
        ["diag_not_a_link"] = new(
            "Это не похоже ни на ссылку, ни на путь к файлу. Нужен адрес вида https://… либо ссылка на ноду (vless://, hy2://), либо путь к файлу с конфигурацией.",
            "This looks like neither a link nor a file path. Use an https://… address, a node link (vless://, hy2://), or a path to a configuration file."),
        ["diag_file_bad"] = new(
            "Файл открылся, но нод в нём не нашлось. Проверьте, что это конфигурация или список ссылок, а не что-то другое.",
            "The file opened but no nodes were found in it. Check that it is a configuration or a list of links."),
        ["diag_node_uri_bad"] = new(
            "Ссылка на ноду разобралась, но нод в ней не оказалось — похоже, она обрезана.",
            "The node link was parsed but contained no node — it looks truncated."),
        ["ask_sub_retry"] = new("Ввести другую ссылку?", "Enter a different link?"),
        ["ask_apps_none_found"] = new(
            "Знакомых программ не нашлось. Впишите путь к той, которой нужен туннель.",
            "No familiar programs found. Enter the path to the one that needs the tunnel."),
        ["ask_apps_found"] = new(
            "Нашлось на этом компьютере — что отправить в туннель?",
            "Found on this machine — what should go through the tunnel?"),
        ["ask_protect"] = new(
            "Включить защиту и запускать её при старте системы?",
            "Turn protection on and start it with the system?"),
        ["ask_protect_root"] = new(
            "Для включения нужны права: {0}chp daemon — либо {0}chp autostart on",
            "Turning it on requires privileges: {0}chp daemon, or {0}chp autostart on"),
        ["ask_country_menu"] = new(
            "Номер строки переключает страну, Enter — выйти",
            "A row number toggles the country, Enter exits"),
        ["ask_port"] = new("Порт панели управления", "Control panel port"),
        ["ask_lang"] = new("Язык интерфейса / interface language", "Язык интерфейса / interface language"),
        ["ask_nothing_to_pick"] = new("Выбирать не из чего.", "There is nothing to pick."),
        ["state_more"] = new("Все команды: chp help", "All commands: chp help"),
        ["state_first_run"] = new(
            "Первый запуск. Пройдём настройку — это пять вопросов.",
            "First run. Let us go through the setup — five questions."),
        ["need_root_setup"] = new(
            "Настройки хранятся в общей папке ({0}), поэтому настройка идёт от администратора: {1}chp setup",
            "The settings live in a shared folder ({0}), so the setup runs as administrator: {1}chp setup"),
        ["hint_after_add"] = new(
            "Программу перезапустите — её соединения пойдут по новым правилам.",
            "Restart the program so its connections follow the new rules."),

        // ── мастер настройки ─────────────────────────────────────────
        ["setup_password_why"] = new(
            "Панель и команды доступны всем, кто вошёл на этот компьютер. Если он общий — поставьте пароль.",
            "The panel and the commands are available to anyone logged into this machine. Set a password if it is shared."),
        ["setup_shared_ask"] = new(
            "Этим компьютером пользуется кто-то ещё?", "Does anyone else use this computer?"),
        ["setup_password_again"] = new("Повторите пароль", "Repeat the password"),
        ["setup_password_short"] = new(
            "Слишком короткий, нужно хотя бы 4 знака.", "Too short, at least 4 characters."),
        ["setup_password_mismatch"] = new("Пароли не совпали.", "Passwords do not match."),
        ["setup_autostart"] = new(
            "Запускать при старте системы?", "Start with the system?"),
        ["setup_done"] = new(
            "Готово.", "Done."),
        ["setup_open_hint"] = new(
            "Открыть её командой: chp open. Если защита ещё не включена — {0}chp daemon",
            "Open it with: chp open. If protection is not on yet — {0}chp daemon"),
        ["setup_hint"] = new(
            "Первый запуск: пройдите настройку командой chp setup",
            "First run: go through the wizard with chp setup"),
        ["autostart_state_on"] = new("автозапуск включён", "autostart enabled"),
        ["on_word"] = new("включён", "enabled"),
        ["off_word"] = new("выключен", "disabled"),
        ["autostart_state_off"] = new("автозапуск выключен", "autostart disabled"),

        // ── проверка изоляции ────────────────────────────────────────
        ["verify_isolated"] = new(
            "ИЗОЛИРОВАНО: все соединения идут через туннель",
            "ISOLATED: every connection goes through the tunnel"),
        ["verify_leaking"] = new(
            "ТЕЧЁТ: часть соединений идёт мимо туннеля",
            "LEAKING: some connections bypass the tunnel"),
        ["verify_counts"] = new(
            "процессов: {0}, через туннель: {1}, напрямую: {2}",
            "processes: {0}, tunneled: {1}, direct: {2}"),
        ["verify_not_running"] = new(
            "Программа сейчас не запущена. Запустите её, дайте ей выйти в сеть и повторите проверку.",
            "The program is not running. Start it, let it reach the network and repeat the check."),
        ["verify_no_conn"] = new(
            "У программы нет активных соединений. Поработайте в ней и повторите проверку.",
            "The program has no active connections. Use it for a moment and repeat the check."),
    };
}

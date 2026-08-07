using ProxyCage.Core;

namespace ProxyCage.Cli;

/// <summary>
/// Вспомогательное для терминала: доступ по паролю, диалог первичной настройки,
/// печать таблиц. Вынесено из Program.cs, чтобы разбор команд читался подряд.
/// </summary>
public static class Cli
{
    public static string Lang(CehoConfig cfg) => cfg.Language;
    public static string S(CehoConfig cfg, string key, params object[] a) => Strings.T(cfg.Language, key, a);

    /// <summary>
    /// Полный список команд. Он больше не выводится по «chp» без аргументов: там теперь
    /// состояние и вопросы по делу, а справочник — по запросу, «chp help».
    /// </summary>
    public static void PrintHelp(CehoConfig cfg)
    {
        var sudo = Os.IsWindows ? "" : "sudo ";
        var en = Lang(cfg) == "en";

        // колонки считаем, а не подбираем пробелами руками: на Windows префикса sudo нет,
        // и захардкоженные отступы разъезжались ровно там, где он был
        (string Title, (string Cmd, string What)[] Rows)[] groups = en
        ? [
            ("Every day", [
                ("chp", "state and what to do next"),
                ("chp status", "state and the real exit IP"),
                ("chp verify", "prove isolation by live connections"),
                ("chp open", "open the panel in a browser"),
            ]),
            ("Setup", [
                ("chp setup", "go through the setup again"),
                ("chp add-app [path]", "isolate a program (no path — pick from a list)"),
                ("chp apps · chp remove-app", "list and remove"),
                ("chp sub-add [name link]", "add a subscription (no arguments — I will ask)"),
                ("chp subs · chp sub-remove", "list and remove"),
                ("chp countries · chp country", "exit countries"),
                ("chp speed <ms> · speed off", "drop nodes slower than this"),
                ("chp passwd · lang · set-port", "password, language, panel port"),
                (sudo + "chp autostart on|off", "start with the system"),
            ]),
            ("Tunnel", [
                (sudo + "chp daemon", "turn protection and the panel on"),
                (sudo + "chp stop", "turn protection off"),
                ("chp run <command>", "run a command through the tunnel once"),
                ("chp wrap · unwrap · wrapped", "route a command permanently"),
                ("chp browser", "proxy settings for a browser"),
            ]),
            ("Other", [
                ("chp doctor", "check what is missing before start"),
                ("chp detect", "find installed AI tools"),
                ("chp apply", "rebuild the rules"),
                ("chp update · version", "update and version"),
                (sudo + "chp uninstall", "remove everything"),
            ]),
        ]
        : [
            ("Каждый день", [
                ("chp", "состояние и что делать дальше"),
                ("chp status", "состояние и реальный IP выхода"),
                ("chp verify", "доказать изоляцию по живым соединениям"),
                ("chp open", "открыть панель в браузере"),
            ]),
            ("Настройка", [
                ("chp setup", "пройти настройку заново"),
                ("chp add-app [путь]", "изолировать программу (без пути — выбор из списка)"),
                ("chp apps · chp remove-app", "список и удаление"),
                ("chp sub-add [имя ссылка]", "добавить подписку (без аргументов — спрошу)"),
                ("chp subs · chp sub-remove", "список и удаление"),
                ("chp countries · chp country", "страны выхода"),
                ("chp speed <мс> · speed off", "отсеять ноды медленнее порога"),
                ("chp passwd · lang · set-port", "пароль, язык, порт панели"),
                (sudo + "chp autostart on|off", "запуск при старте системы"),
            ]),
            ("Туннель", [
                (sudo + "chp daemon", "включить защиту и панель"),
                (sudo + "chp stop", "выключить защиту"),
                ("chp run <команда>", "разово запустить команду через туннель"),
                ("chp wrap · unwrap · wrapped", "перевести команду на туннель насовсем"),
                ("chp browser", "настройки прокси для браузера"),
            ]),
            ("Прочее", [
                ("chp doctor", "проверить, всё ли готово к запуску"),
                ("chp detect", "найти установленные ИИ-инструменты"),
                ("chp apply", "пересобрать правила"),
                ("chp update · version", "обновление и версия"),
                (sudo + "chp uninstall", "удалить всё"),
            ]),
        ];

        var width = groups.SelectMany(g => g.Rows).Max(r => r.Cmd.Length) + 2;
        Console.WriteLine();
        Console.WriteLine("  " + S(cfg, "tagline"));
        foreach (var g in groups)
        {
            Console.WriteLine();
            Console.WriteLine("  " + g.Title);
            foreach (var (cmd, what) in g.Rows)
                Console.WriteLine("    " + cmd.PadRight(width) + what);
        }
        Console.WriteLine();
        Console.WriteLine("  " + (en
            ? "Add --password \"secret\" to any command if a password is set and you are not an administrator."
            : "Если пароль задан, а вы не администратор, добавляйте к команде --password «пароль»."));
        Console.WriteLine();
        Console.WriteLine("  " + S(cfg, "product_page_at", Brand.RepoUrl(cfg.UpdateRepo)));
    }

    /// <summary>
    /// Модель доступа. Права системы уже закрывают файл настроек от посторонних,
    /// поэтому администратора паролем не мучаем — он всё равно может править файл руками.
    /// Пароль спрашивается у обычного пользователя: на терминальном сервере таких много,
    /// и настройки изоляции — не их дело.
    /// </summary>
    public static bool Allowed(CehoConfig cfg, string[] args, out string? error)
    {
        error = null;
        if (!Auth.HasPassword(cfg) || Os.IsElevated()) return true;

        var given = Opt(args, "--password") ?? Environment.GetEnvironmentVariable("CEHOPROXY_PASSWORD");
        if (Auth.Verify(cfg, given)) return true;

        error = given is null ? S(cfg, "auth_needed_cli") : S(cfg, "auth_wrong");
        return false;
    }

    public static string? Opt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static string Ask(string prompt, string? fallback = null)
    {
        Console.Write(fallback is null ? $"{prompt}: " : $"{prompt} [{fallback}]: ");
        var line = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(line) ? fallback ?? "" : line;
    }

    public static bool AskYes(string prompt, bool fallback)
    {
        var hint = fallback ? "Y/n" : "y/N";
        Console.Write($"{prompt} [{hint}]: ");
        var line = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(line)) return fallback;
        return line is "y" or "yes" or "д" or "да";
    }

    /// <summary>Ввод пароля без эха. Без терминала (запуск из службы) молча возвращает пусто.</summary>
    public static string AskSecret(string prompt)
    {
        Console.Write($"{prompt}: ");
        if (Console.IsInputRedirected) { Console.WriteLine(); return Console.ReadLine() ?? ""; }

        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
        Console.WriteLine();
        return buffer.ToString();
    }

    /// <summary>Команды, которые можно выполнять через панель. Всё остальное — только локально.</summary>
    private static readonly HashSet<string> RemoteAllowed = new(StringComparer.Ordinal)
    {
        "status", "doctor", "verify", "apps", "add-app", "remove-app",
        "subs", "sub-add", "sub-remove", "countries", "country", "nodes",
        "browser", "detect", "apply", "lang", "set-port", "autostart", "speed",
    };

    public static bool CanRunRemotely(string command) => RemoteAllowed.Contains(command);

    /// <summary>
    /// Настройки нам недоступны: либо закрыт сам файл, либо папка целиком.
    ///
    /// Проверять только File.Exists нельзя: при закрытой папке он возвращает false, и код
    /// уходил бы в «ещё не настроено», показывая человеку пустой список вместо отказа.
    /// Признак установленной системы — публичный указатель на панель рядом с настройками.
    /// </summary>
    public static bool ConfigUnreadable(string configPath)
    {
        try
        {
            using var _ = File.OpenRead(configPath);
            return false;                      // читаем — значит доступ есть
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch { return true; }                 // есть, но не наш

        // файла не видно: он либо не создан, либо спрятан закрытой папкой
        var root = Path.GetDirectoryName(configPath) ?? ".";
        return Auth.ReadPanelPointer(root) is not null || !Directory.Exists(root) && DirectoryHidden(root);
    }

    private static bool DirectoryHidden(string root)
    {
        try { Directory.GetFiles(root); return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch { return true; }
    }

    /// <summary>Команды, которые меняют настройки: им мало прочитать файл, нужно и записать.</summary>
    private static readonly HashSet<string> Mutating = new(StringComparer.Ordinal)
    {
        "add-app", "remove-app", "sub-add", "sub-remove", "country", "set-port",
        "lang", "passwd", "apply", "autostart", "uninstall", "speed",
    };

    public static bool ChangesSettings(string command) => Mutating.Contains(command);

    /// <summary>
    /// Файл настроек читается, но записать в него мы не можем.
    ///
    /// Проверять только чтение мало: у обычного пользователя общей машины файл нередко
    /// доступен на чтение и закрыт на запись, и команда падала с системной ошибкой
    /// вместо понятного отказа. Поймано живьём.
    /// </summary>
    public static bool ConfigReadOnly(string configPath)
    {
        if (!File.Exists(configPath)) return false;
        try
        {
            using var _ = new FileStream(configPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Терминал без прав на настройки работает через панель: команда уходит службе,
    /// выполняется от её имени и возвращает тот же текст, что напечатал бы локальный запуск.
    /// Так CLI остаётся полноценным и на сервере, где человек не администратор.
    /// </summary>
    public static async Task<int> RunRemoteAsync(string root, string[] args, string lang)
    {
        var port = Auth.ReadPanelPointer(root);
        if (port is null)
        {
            Console.Error.WriteLine(Strings.T(lang, "remote_no_panel", Os.IsWindows ? "" : "sudo "));
            return 1;
        }

        var payload = string.Join("\n", args.Where((a, i) =>
            !(a == "--password" || (i > 0 && args[i - 1] == "--password"))));

        var password = Opt(args, "--password") ?? Environment.GetEnvironmentVariable("CEHOPROXY_PASSWORD");

        // сначала пробуем без пароля: он мог быть и не задан, а спрашивать заранее —
        // значит подвесить команду на машине, где пароля нет вовсе
        var (status, body) = await CallApiAsync(port.Value, payload, password ?? "", lang);
        if (status == 401 && password is null && !Console.IsInputRedirected)
        {
            password = AskSecret(Strings.T(lang, "auth_password"));
            (status, body) = await CallApiAsync(port.Value, payload, password, lang);
        }

        if (status == 0) { Console.Error.WriteLine(body); return 1; }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var ok = doc.RootElement.GetProperty("ok").GetBoolean();
            var message = doc.RootElement.GetProperty("text").GetString() ?? "";
            if (message.Length > 0) (ok ? Console.Out : Console.Error).WriteLine(message.TrimEnd());
            return ok ? 0 : 1;
        }
        catch
        {
            Console.Error.WriteLine(Strings.T(lang, "remote_failed", body));
            return 1;
        }
    }

    /// <summary>0 в статусе — до панели вообще не достучались, и в теле человеческая причина.</summary>
    private static async Task<(int Status, string Body)> CallApiAsync(
        int port, string payload, string password, string lang)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Ceho-Password", password);
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api",
                new StringContent(payload, System.Text.Encoding.UTF8, "text/plain"));
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException)
        {
            return (0, Strings.T(lang, "remote_no_panel", Os.IsWindows ? "" : "sudo "));
        }
        catch (Exception ex)
        {
            return (0, Strings.T(lang, "remote_failed", ex.Message));
        }
    }

    /// <summary>
    /// Короткая команда chp рядом с самим бинарником. Симлинк, а не алиас оболочки:
    /// алиас живёт только в интерактивной сессии и не виден скриптам, службам и PowerShell.
    /// </summary>
    public static string? MakeShortcut(string exePath, out string? created)
    {
        created = null;
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir)) return "не удалось определить папку программы";

            if (Os.IsWindows)
            {
                // .cmd работает и в cmd, и в PowerShell, и не требует правки профилей
                var cmd = Path.Combine(dir, "chp.cmd");
                File.WriteAllText(cmd, "@echo off\r\n\"" + exePath + "\" %*\r\n");
                created = cmd;
                return null;
            }

            var link = Path.Combine(dir, "chp");
            if (File.Exists(link) || Directory.Exists(link)) File.Delete(link);
            File.CreateSymbolicLink(link, exePath);
            created = link;
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Запуск команды через туннель переменными окружения.
    ///
    /// Нужно для терминальных агентов: у них нет своей программы, их запускает общий
    /// интерпретатор, и правило по пути совпадёт с интерпретатором, а не с агентом.
    /// Проверено живьём: curl слушается HTTPS_PROXY сам, а node — только с NODE_USE_ENV_PROXY.
    /// </summary>
    public static int RunThroughTunnel(int proxyPort, string[] argv)
    {
        var proxy = $"http://127.0.0.1:{proxyPort}";
        var psi = new System.Diagnostics.ProcessStartInfo(argv[0]) { UseShellExecute = false };
        for (var i = 1; i < argv.Length; i++) psi.ArgumentList.Add(argv[i]);

        foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY",
                                    "http_proxy", "https_proxy", "all_proxy" })
            psi.Environment[key] = proxy;
        psi.Environment["NO_PROXY"] = "localhost,127.0.0.1,::1";
        psi.Environment["no_proxy"] = "localhost,127.0.0.1,::1";
        // без этого node игнорирует переменные прокси — проверено на node 22
        psi.Environment["NODE_USE_ENV_PROXY"] = "1";

        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return 1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 127;
        }
    }

    /// <summary>Куда класть обёртки: каталог пользователя, права администратора не нужны.</summary>
    public static string WrapDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Os.IsWindows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CehoProxy", "bin")
            : Path.Combine(home, ".local", "bin");
    }

    public static string WrapPath(string name) =>
        Path.Combine(WrapDir(), Os.IsWindows ? name + ".cmd" : name);

    /// <summary>
    /// Постоянный перевод команды на туннель: рядом с оригиналом появляется обёртка с тем же
    /// именем, которая вызывает его через прокси. Дальше человек пишет привычное «gemini»,
    /// а трафик идёт в туннель. Остальные программы, включая другие на том же интерпретаторе,
    /// не затрагиваются.
    /// </summary>
    public static string? Wrap(string name, out string? note)
    {
        note = null;
        var real = Os.FindOnPath(Os.IsWindows ? name + ".exe" : name) ?? Os.FindOnPath(name);
        if (real is null) return $"команда «{name}» в PATH не найдена";

        var dir = WrapDir();
        var wrapper = WrapPath(name);

        // не оборачивать собственную обёртку, иначе получится бесконечный вызов
        if (Os.RealPath(real).Equals(Os.RealPath(wrapper), StringComparison.OrdinalIgnoreCase))
            return $"«{name}» уже переведена на туннель";

        try
        {
            Directory.CreateDirectory(dir);
            var self = Ceho.OwnExecutablePath;

            if (Os.IsWindows)
                File.WriteAllText(wrapper,
                    "@echo off\r\nrem CehoProxy: эта команда ходит в интернет через туннель\r\n\""
                    + self + "\" run \"" + real + "\" %*\r\n");
            else
            {
                File.WriteAllText(wrapper,
                    "#!/bin/sh\n# CehoProxy: эта команда ходит в интернет через туннель\n" +
                    "exec \"" + self + "\" run \"" + real + "\" \"$@\"\n");
                Os.Run("chmod", $"755 {wrapper}", 5000);
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        // обёртка сработает, только если её каталог идёт в PATH РАНЬШЕ оригинала
        if (!ShadowsOriginal(dir, real))
            note = $"каталог {dir} идёт в PATH после {Path.GetDirectoryName(real)}, " +
                   "поэтому обёртка не перехватит команду. Поставьте его раньше в PATH " +
                   "или вызывайте её полным путём: " + wrapper;

        return null;
    }

    private static bool ShadowsOriginal(string wrapDir, string realPath)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.TrimEnd(Path.DirectorySeparatorChar)).ToList();

        var realDir = Path.GetDirectoryName(realPath)?.TrimEnd(Path.DirectorySeparatorChar) ?? "";
        var wrapIndex = dirs.FindIndex(d => d.Equals(wrapDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
        var realIndex = dirs.FindIndex(d => d.Equals(realDir, StringComparison.OrdinalIgnoreCase));

        return wrapIndex >= 0 && (realIndex < 0 || wrapIndex < realIndex);
    }

    public static string? Unwrap(string name)
    {
        var wrapper = WrapPath(name);
        if (!File.Exists(wrapper)) return $"«{name}» не переведена на туннель";
        try { File.Delete(wrapper); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Список переведённых команд: узнаём их по нашей подписи внутри файла.</summary>
    public static IReadOnlyList<string> Wrapped()
    {
        try
        {
            return Directory.EnumerateFiles(WrapDir())
                .Where(IsOurWrapper)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n is not null && n != "cehoproxy" && n != "chp")
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Наша обёртка узнаётся по подписи внутри файла, а не по имени.</summary>
    private static bool IsOurWrapper(string file)
    {
        try { return File.ReadAllText(file).Contains("CehoProxy", StringComparison.Ordinal); }
        catch { return false; }
    }

    /// <summary>Флаг страны, а для группы «страна не определена» — пустое место той же ширины.</summary>
    public static string FlagCell(string code) =>
        code == CountryResolver.Unknown ? "  " : CountryResolver.Flag(code);

    /// <summary>Страны пула с галочками. Ноды без распознанной страны — отдельной строкой «??».</summary>
    public static void PrintCountries(CehoConfig cfg, IReadOnlyList<ProxyNode> nodes)
    {
        Console.WriteLine(S(cfg, "countries_title"));
        Console.WriteLine();
        foreach (var (code, name, count, i) in CountryRows(cfg, nodes))
            Console.WriteLine($"  {i + 1,2}. [{(CountryEnabled(cfg, code) ? "x" : " ")}] " +
                              $"{FlagCell(code)} {code,-3} {name,-22} " +
                              S(cfg, "col_nodes").ToLowerInvariant() + $": {count}");
    }

    private static List<(string Code, string Name, int Count, int Index)> CountryRows(
        CehoConfig cfg, IReadOnlyList<ProxyNode> nodes) =>
        nodes.GroupBy(n => n.CountryCode ?? CountryResolver.Unknown)
            .OrderByDescending(g => g.Count())
            .Select((g, i) => (
                g.Key,
                g.Key == CountryResolver.Unknown
                    ? S(cfg, "country_unknown")
                    : g.First().CountryName ?? g.Key,
                g.Count(),
                i))
            .ToList();

    /// <summary>
    /// Список стран, где строка включается и выключается номером. Настройка, при которой
    /// в пуле не остаётся ни одной ноды, откатывается: уйти с неработающим туннелем нельзя.
    /// </summary>
    public static async Task<int> CountryMenuAsync(CehoConfig cfg)
    {
        IReadOnlyList<ProxyNode> nodes;
        try { nodes = await Ceho.LoadAllNodesAsync(cfg); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

        while (true)
        {
            Console.WriteLine();
            PrintCountries(cfg, nodes);
            Console.WriteLine();
            var answer = Ask("  " + S(cfg, "ask_country_menu"));
            if (answer.Length == 0) break;

            var rows = CountryRows(cfg, nodes);
            if (!int.TryParse(answer, out var n) || n < 1 || n > rows.Count)
            {
                Console.WriteLine("  " + S(cfg, "ask_bad_choice"));
                continue;
            }

            var code = rows[n - 1].Code;
            var beforeExcluded = new List<string>(cfg.ExcludedCountries);
            var beforePreferred = new List<string>(cfg.PreferredCountries);

            if (CountryEnabled(cfg, code))
            {
                if (!cfg.ExcludedCountries.Contains(code, StringComparer.OrdinalIgnoreCase))
                    cfg.ExcludedCountries.Add(code);
                cfg.PreferredCountries.RemoveAll(c => c.Equals(code, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                cfg.ExcludedCountries.RemoveAll(c => c.Equals(code, StringComparison.OrdinalIgnoreCase));
                if (cfg.PreferredCountries.Count > 0
                    && !cfg.PreferredCountries.Contains(code, StringComparer.OrdinalIgnoreCase))
                    cfg.PreferredCountries.Add(code);
            }

            try
            {
                SingBoxConfigGenerator.FilterByCountries(nodes, cfg);
                cfg.Save(Ceho.ConfigPath);
            }
            catch (PoolEmptyException ex)
            {
                cfg.ExcludedCountries = beforeExcluded;
                cfg.PreferredCountries = beforePreferred;
                Console.WriteLine("  " + ex.Message);
                Console.WriteLine("  " + S(cfg, "change_reverted"));
            }
        }

        try { Console.WriteLine("  " + await Ceho.ApplyAsync()); }
        catch (Exception ex) { Console.Error.WriteLine("  " + ex.Message); return 1; }
        return 0;
    }

    /// <summary>
    /// Замер задержек и отсев медленных нод.
    ///
    /// Мерим один раз и запоминаем: делать это при каждой сборке правил значило бы ждать
    /// на ровном месте. Порог хранится отдельно от замеров, поэтому его можно менять
    /// без повторного замера. Возвращает число оставшихся нод, либо null — если замеру
    /// нельзя верить и трогать настройки не за что.
    /// </summary>
    public static async Task<int?> MeasureAndFilterAsync(CehoConfig cfg, int limitMs)
    {
        IReadOnlyList<ProxyNode> nodes;
        try { nodes = await Ceho.LoadAllNodesAsync(cfg); }
        catch (Exception ex) { Console.WriteLine("  " + ex.Message); return null; }

        Console.WriteLine("  " + S(cfg, "speed_measuring"));
        var measured = await Task.WhenAll(nodes.Select(n => NodeProbe.MeasureAsync(n)));

        // числа, полученные из-под чужого туннеля, не про скорость нод — по ним не отсеиваем
        if (NodeProbe.LooksLikeLocalAccept(measured))
        {
            Console.WriteLine("  " + S(cfg, "speed_local_accept"));
            return null;
        }

        foreach (var m in measured)
            if (m.LatencyMs is { } ms) cfg.NodeLatency[m.Node.Key] = ms;

        cfg.MaxLatencyMs = limitMs;

        var slow = measured.Count(m => m.LatencyMs is { } ms && ms > limitMs);
        var left = nodes.Count(n => !SingBoxConfigGenerator.IsTooSlow(n, cfg));
        Console.WriteLine("  " + S(cfg, "speed_result",
            measured.Count(m => m.LatencyMs is not null), slow, left));
        if (measured.Any(m => m.LatencyMs is null))
            Console.WriteLine("  " + S(cfg, "speed_unmeasured_note"));
        return left;
    }

    /// <summary>
    /// Пересобрать правила после изменения настроек.
    ///
    /// Раньше это была отдельная команда «apply», и человек уходил с уверенностью, что
    /// программа уже в туннеле, хотя правила остались прежними. Ошибку показываем,
    /// но кодом возврата не караем: сама-то настройка сохранена.
    /// </summary>
    public static async Task RebuildQuietlyAsync(CehoConfig cfg)
    {
        try { Console.WriteLine(await Ceho.ApplyAsync()); }
        catch (Exception ex) { Stuck(cfg, ex.Message); return; }

        // Пересобрать правила мало: работающая защита продолжает жить по старым.
        // Просить человека «перезапустите» — значит оставить ловушку: он добавил
        // программу, увидел «готово» и уверен, что она в туннеле. Перезапускаем сами.
        if (!DaemonControl.IsRunning(Ceho.Root)) return;
        if (!Os.IsElevated()) { Console.WriteLine(S(cfg, "rules_restart_needed", Os.IsWindows ? "" : "sudo ")); return; }

        Autostart.Restart();
        Console.WriteLine(S(cfg, "rules_applied"));
    }

    /// <summary>
    /// Отказ, после которого человеку надо что-то сделать. Одного «не добавлено ни одной
    /// подписки» мало: из него не следует, что делать дальше. Подсказываем самый короткий путь.
    /// </summary>
    public static void Stuck(CehoConfig cfg, string message)
    {
        Console.Error.WriteLine(message);
        if (cfg.Subscriptions.Count == 0 || cfg.Apps.Count == 0)
            Console.Error.WriteLine(S(cfg, "hint_ask_me"));
    }

    public static bool CountryEnabled(CehoConfig cfg, string code) =>
        !cfg.ExcludedCountries.Contains(code, StringComparer.OrdinalIgnoreCase)
        && (cfg.PreferredCountries.Count == 0
            || cfg.PreferredCountries.Contains(code, StringComparer.OrdinalIgnoreCase));

    public static void PrintChecks(CehoConfig cfg, IReadOnlyList<Preflight.Check> checks)
    {
        foreach (var c in checks)
        {
            var mark = c.Level switch
            {
                Preflight.Level.Ok => "[ок]   ",
                Preflight.Level.Warning => "[но]   ",
                _ => "[стоп] ",
            };
            Console.WriteLine(mark + c.Title);
            if (c.Detail is not null) Console.WriteLine("       " + c.Detail);
            if (c.Fix is not null)
                Console.WriteLine("       " + (Lang(cfg) == "en" ? "What to do: " : "Что делать: ") + c.Fix);
        }
    }

    /// <summary>
    /// Мастер первичной настройки: пять вопросов, каждый со значением по умолчанию.
    ///
    /// Порты отсюда убраны намеренно: у них есть рабочие умолчания, менять их нужно редко
    /// и для этого есть отдельная команда. Пароль спрашивается не сам по себе, а через
    /// вопрос о том, общий ли это компьютер: на личном он лишняя морока, на общем обязателен.
    /// </summary>
    public static async Task<int> SetupAsync(string configPath)
    {
        var cfg = CehoConfig.Load(configPath);

        Console.WriteLine();
        Console.WriteLine("  CehoProxy · настройка / setup");
        Console.WriteLine("  ─────────────────────────────");
        Console.WriteLine();

        cfg.Language = Strings.Normalize(
            Ask("  Язык интерфейса / interface language (ru/en)", cfg.Language));
        Console.WriteLine();

        // 1. подписка — и сразу проверка, что по ссылке действительно есть ноды
        await Assistant.AskSubscriptionAsync(cfg);

        // 2. отсев медленных нод — предложение, а не умолчание: замер занимает время,
        // и не всем он нужен
        if (cfg.Subscriptions.Count > 0 && AskYes("  " + S(cfg, "speed_ask"), false))
        {
            var limit = Ask("  " + S(cfg, "speed_limit"), "500");
            if (int.TryParse(limit, out var ms) && ms > 0)
            {
                var left = await MeasureAndFilterAsync(cfg, ms);
                if (left == 0) Console.WriteLine("  " + S(cfg, "speed_none_left", ms));
                if (left is null or 0) cfg.MaxLatencyMs = null;
            }
            Console.WriteLine();
        }

        // 3. что изолировать — из найденного на этой машине
        Assistant.AskApps(cfg);
        Console.WriteLine();

        // 4. общий компьютер → пароль обязателен, личный → не спрашиваем вовсе
        if (AskYes("  " + S(cfg, "setup_shared_ask"), false))
        {
            Console.WriteLine("  " + S(cfg, "setup_password_why"));
            while (true)
            {
                var pass = AskSecret("  " + S(cfg, "auth_password"));
                if (pass.Length < 4) { Console.WriteLine("  " + S(cfg, "setup_password_short")); continue; }
                var again = AskSecret("  " + S(cfg, "setup_password_again"));
                if (pass != again) { Console.WriteLine("  " + S(cfg, "setup_password_mismatch")); continue; }
                Auth.SetPassword(cfg, pass);
                Console.WriteLine("  " + S(cfg, "auth_set_ok"));
                break;
            }
        }

        // без движка туннель не поднимется, а искать его самому — тупик на ровном месте.
        // Качает его пользователь своим согласием: раздавать чужой GPL-бинарник мы не вправе
        if (Os.ResolveSingBox(Ceho.Root) is null && Os.IsElevated()
            && AskYes("  " + S(cfg, "inst_engine_ask"), true))
        {
            try { await Installer.DownloadEngineAsync(Ceho.Root, m => Console.WriteLine("  " + m), cfg.Language); }
            catch (Exception ex)
            {
                Console.WriteLine("  " + S(cfg, "inst_engine_failed", ex.Message));
                Console.WriteLine("  " + S(cfg, "inst_engine_skip"));
            }
        }

        cfg.SetupDone = true;
        cfg.Save(configPath);
        Auth.RestrictConfigAccess(configPath);

        if (cfg.Subscriptions.Count > 0 && cfg.Apps.Count > 0)
        {
            try { Console.WriteLine("  " + await Ceho.ApplyAsync()); }
            catch (Exception ex) { Console.WriteLine("  " + ex.Message); }
        }

        // 5. автозапуск: он же и включает защиту прямо сейчас — со всеми нужными правами
        Console.WriteLine();
        if (AskYes("  " + S(cfg, "setup_autostart"), true))
        {
            var err = Autostart.Enable(Ceho.OwnExecutablePath, Ceho.Root);
            if (err is not null) Console.WriteLine("  " + err);
            else
            {
                Autostart.Restart();
                Console.WriteLine("  " + S(cfg, "autostart_state_on"));
            }
        }

        // если короткая команда уже есть в PATH (её сделал установщик), второй раз не создаём:
        // иначе рядом с временной копией программы остаётся мусорный ярлык
        if (Os.FindOnPath(Os.IsWindows ? "chp.cmd" : "chp") is { } existing)
            Console.WriteLine("  " + S(cfg, "alias_made", existing));
        else
        {
            var shortcutError = MakeShortcut(Ceho.OwnExecutablePath, out var shortcut);
            Console.WriteLine("  " + (shortcutError is null
                ? S(cfg, "alias_made", shortcut ?? "chp")
                : S(cfg, "alias_failed", shortcutError)));
        }

        var blockers = Preflight.Run(cfg, Ceho.Root).Where(c => c.Level == Preflight.Level.Blocker).ToList();
        if (blockers.Count > 0)
        {
            Console.WriteLine();
            PrintChecks(cfg, blockers);
        }

        Console.WriteLine();
        // «Готово» сразу после двух [стоп] — враньё: настройка не закончена, и человек
        // должен уйти отсюда с понятным следующим шагом, а не с ложным успехом
        Console.WriteLine("  " + S(cfg, blockers.Count > 0 ? "setup_unfinished" : "setup_done"));
        Console.WriteLine();
        Console.WriteLine("  " + Strings.T(cfg.Language, "panel_at", $"http://127.0.0.1:{cfg.WebPort}"));
        Console.WriteLine("  " + S(cfg, "setup_open_hint", Os.IsWindows ? "" : "sudo "));
        Console.WriteLine("  " + S(cfg, "product_page_at", Brand.RepoUrl(cfg.UpdateRepo)));
        return 0;
    }
}

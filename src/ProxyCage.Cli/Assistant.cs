using ProxyCage.Core;

namespace ProxyCage.Cli;

/// <summary>
/// Что происходит, когда человек набрал просто «chp».
///
/// Раньше вываливался список из тридцати команд, и дальше надо было догадываться,
/// какие из них нужны именно сейчас. Здесь наоборот: показываем состояние в пять строк
/// и спрашиваем ровно о том, чего не хватает. Список команд остался, но по запросу — «chp help».
///
/// Все вопросы задаются только в живом терминале: в скриптах и службе спрашивать не у кого,
/// там команды ведут себя по-прежнему и возвращают понятный отказ.
/// </summary>
public static class Assistant
{
    public static bool Interactive => !Console.IsInputRedirected;

    public static async Task<int> RunAsync()
    {
        // первый запуск: список команд человеку сейчас не нужен, нужна настройка
        if (!CanWriteSettings(out var cfgOrNull))
        {
            var lang = cfgOrNull?.Language ?? "ru";
            Console.WriteLine(Strings.T(lang, "need_root_setup", Ceho.Root, Os.IsWindows ? "" : "sudo "));
            return 1;
        }

        var cfg = cfgOrNull!;
        if (!cfg.SetupDone)
        {
            Console.WriteLine();
            Console.WriteLine("  " + Cli.S(cfg, "state_first_run"));
            return await Cli.SetupAsync(Ceho.ConfigPath);
        }

        PrintState(cfg);
        Console.WriteLine();

        var changed = false;
        if (cfg.Subscriptions.Count == 0) changed |= await AskSubscriptionAsync(cfg);
        if (cfg.Apps.Count == 0) changed |= AskApps(cfg);

        if (changed)
        {
            cfg.Save(Ceho.ConfigPath);
            Auth.RestrictConfigAccess(Ceho.ConfigPath);
            try { Console.WriteLine("  " + await Ceho.ApplyAsync()); }
            catch (Exception ex) { Console.WriteLine("  " + ex.Message); }
        }

        if (cfg.Subscriptions.Count > 0 && cfg.Apps.Count > 0 && !DaemonControl.IsRunning(Ceho.Root))
            AskProtection(cfg);

        Console.WriteLine();
        Console.WriteLine("  " + Cli.S(cfg, "state_more"));
        return 0;
    }

    /// <summary>
    /// Настройки лежат в общей папке машины. Если писать в неё нельзя — разговаривать не о чем:
    /// любой ответ человека всё равно некуда сохранить, и честнее сказать это сразу.
    /// </summary>
    private static bool CanWriteSettings(out CehoConfig? cfg)
    {
        cfg = null;
        if (Cli.ConfigUnreadable(Ceho.ConfigPath)) return false;
        try { cfg = CehoConfig.Load(Ceho.ConfigPath); }
        catch { return false; }
        if (File.Exists(Ceho.ConfigPath)) return !Cli.ConfigReadOnly(Ceho.ConfigPath);

        try
        {
            Directory.CreateDirectory(Ceho.Root);
            var probe = Path.Combine(Ceho.Root, ".write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static void PrintState(CehoConfig cfg)
    {
        var daemon = DaemonControl.IsRunning(Ceho.Root);
        var tunnel = NodeProbe.TunnelIsUp(cfg.TunAddress);
        var up = daemon && tunnel;

        Console.WriteLine();
        Console.WriteLine("  CehoProxy " + Updater.CurrentVersion + " · " +
            (up ? Cli.S(cfg, "state_on") : daemon ? Cli.S(cfg, "state_broken") : Cli.S(cfg, "state_off")));
        if (!daemon && tunnel)
            Console.WriteLine("  " + Cli.S(cfg, "state_leftovers", Os.IsWindows ? "" : "sudo "));
        Console.WriteLine();

        var subs = cfg.Subscriptions.Count == 0
            ? Cli.S(cfg, "empty")
            : string.Join(", ", cfg.Subscriptions.Select(s =>
                s.Name + " — " + (s.LastCheckOk switch
                {
                    true => Cli.S(cfg, "sub_ok"),
                    false => Cli.S(cfg, "sub_bad"),
                    null => Cli.S(cfg, "sub_unchecked"),
                })));

        var apps = cfg.Apps.Count == 0
            ? Cli.S(cfg, "empty")
            : string.Join(", ", cfg.Apps.Where(a => a.Enabled).Select(a => a.Name));

        var countries = cfg.PreferredCountries.Count > 0
            ? string.Join(", ", cfg.PreferredCountries)
            : cfg.ExcludedCountries.Count > 0
                ? Cli.S(cfg, "country_any_but", string.Join(", ", cfg.ExcludedCountries))
                : Cli.S(cfg, "country_any");

        Row(Cli.S(cfg, "nav_subs"), subs);
        Row(Cli.S(cfg, "nav_apps"), apps);
        Row(Cli.S(cfg, "nav_exit"), countries);
        Row(Cli.S(cfg, "speed_row"), cfg.MaxLatencyMs is { } ms
            ? Cli.S(cfg, "speed_state_on", ms)
            : Cli.S(cfg, "speed_row_off"));
        Row(Cli.S(cfg, "autostart_title"),
            Cli.S(cfg, Autostart.IsEnabled() ? "on_word" : "off_word"));
        Row("Web", $"http://127.0.0.1:{cfg.WebPort}");
        Row(Cli.S(cfg, "product_page"), Brand.RepoUrl(cfg.UpdateRepo));
    }

    private static void Row(string name, string value) =>
        Console.WriteLine($"  {name,-18} {value}");

    // ── чего не хватает ───────────────────────────────────────────────

    /// <summary>
    /// Подписка спрашивается и тут же проверяется: скачиваем, разбираем и показываем,
    /// сколько нод и каких стран нашлось. Иначе человек узнаёт о нерабочей ссылке
    /// только при включении защиты, и связать одно с другим уже сложно.
    /// </summary>
    public static async Task<bool> AskSubscriptionAsync(CehoConfig cfg)
    {
        if (!Interactive) return false;

        Console.WriteLine("  " + Cli.S(cfg, "pf_no_subs") + ". " + Cli.S(cfg, "ask_sub_where"));
        while (true)
        {
            var url = Cli.Ask("  " + Cli.S(cfg, "ask_sub_link") + " (" + Cli.S(cfg, "ask_skip") + ")");
            if (url.Length == 0) return false;

            var name = SuggestName(cfg, url);
            cfg.Subscriptions.Add(new SubscriptionEntry { Name = name, Url = url });
            cfg.ActiveSubscription ??= name;

            Console.WriteLine("  " + Cli.S(cfg, "ask_sub_checking"));
            Ceho.Quiet = true;                       // причину скажем сами, разборчиво
            var count = await DescribePoolAsync(cfg, quiet: true);
            Ceho.Quiet = false;
            var entry = cfg.Subscriptions.First(s => s.Name == name);
            entry.LastCheckOk = count > 0;
            entry.LastCheckedUtc = DateTime.UtcNow.ToString("u");
            if (count > 0)
            {
                Console.WriteLine();
                return true;
            }

            cfg.Subscriptions.RemoveAll(s => s.Name == name);
            if (cfg.ActiveSubscription == name) cfg.ActiveSubscription = cfg.Subscriptions.FirstOrDefault()?.Name;
            Console.WriteLine("  " + await Ceho.DiagnoseSubscriptionAsync(url, cfg.Language));
            if (!Cli.AskYes("  " + Cli.S(cfg, "ask_sub_retry"), true)) return false;
        }
    }

    /// <summary>Сколько нод и каких стран отдали подписки. Ноль означает, что пул пуст.</summary>
    public static async Task<int> DescribePoolAsync(CehoConfig cfg, bool quiet = false)
    {
        IReadOnlyList<ProxyNode> nodes;
        try { nodes = await Ceho.LoadAllNodesAsync(cfg); }
        catch (Exception ex)
        {
            if (!quiet) Console.WriteLine("  " + ex.Message);
            return 0;
        }

        Console.WriteLine("  " + Cli.S(cfg, "ask_sub_nodes", nodes.Count));
        foreach (var g in nodes.GroupBy(n => n.CountryCode ?? CountryResolver.Unknown)
                               .OrderByDescending(g => g.Count()))
        {
            var name = g.Key == CountryResolver.Unknown
                ? Cli.S(cfg, "country_unknown")
                : g.First().CountryName ?? g.Key;
            var off = Cli.CountryEnabled(cfg, g.Key) ? "" : "  (" + Cli.S(cfg, "btn_off").ToLowerInvariant() + ")";
            Console.WriteLine($"    {Cli.FlagCell(g.Key)} {name,-22} {g.Count(),3}{off}");
        }
        return nodes.Count;
    }

    private static string SuggestName(CehoConfig cfg, string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";
        var baseName = System.Net.IPAddress.TryParse(host, out _)
            ? "sub"
            : host.Split('.').FirstOrDefault(p => p.Length > 0 && p is not ("www" or "sub")) ?? "sub";
        var name = baseName;
        var i = 2;
        while (cfg.Subscriptions.Any(s => s.Name == name)) name = baseName + i++;
        return name;
    }

    /// <summary>
    /// Программы предлагаем из найденных, а не просим вспоминать путь: у ИИ-инструментов
    /// он длинный и в каждой системе свой. Путь при этом всё равно можно вписать руками.
    /// </summary>
    public static bool AskApps(CehoConfig cfg)
    {
        if (!Interactive) return false;

        var found = AiTools.Detect()
            .Where(t => !cfg.Apps.Any(a => Covers(a, t.Path)))
            .ToList();

        Console.WriteLine("  " + (found.Count > 0
            ? Cli.S(cfg, "ask_apps_found")
            : Cli.S(cfg, "ask_apps_none_found")));

        for (var i = 0; i < found.Count; i++)
        {
            Console.WriteLine($"    {i + 1}. {found[i].Name,-12} {found[i].Path}");
            if (found[i].Kind == AiTools.ToolKind.Script && found[i].Interpreter is not null)
                Console.WriteLine("       " + Cli.S(cfg, "ai_script_warn",
                    Path.GetFileName(found[i].Interpreter!)));
        }

        var hint = (found.Count > 0 ? Cli.S(cfg, "ask_or_path") + ", " : "") + Cli.S(cfg, "ask_skip");
        var answer = Cli.Ask("  " + Cli.S(cfg, "ask_choose") + " (" + hint + ")");
        if (answer.Length == 0) return false;

        var added = false;
        foreach (var part in answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = int.TryParse(part, out var n) && n >= 1 && n <= found.Count
                ? found[n - 1].Path
                : part;
            if (AddApp(cfg, path)) added = true;
        }
        if (added) Console.WriteLine("  " + Cli.S(cfg, "hint_after_add"));
        return added;
    }

    private static bool Covers(AppEntry app, string path) =>
        app.Folder.Equals(path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(app.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Добавление одной программы с человеческим отказом вместо исключения.</summary>
    public static bool AddApp(CehoConfig cfg, string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Console.WriteLine("  " + Cli.S(cfg, "err_no_such_path", path));
            return false;
        }

        AppDetector.Detection d;
        try { d = AppDetector.Detect(path, cfg.Language); }
        catch (InvalidOperationException ex) { Console.WriteLine("  " + ex.Message); return false; }

        if (cfg.Apps.Any(a => a.Folder.Equals(d.Folder, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("  " + Cli.S(cfg, "err_already_added"));
            return false;
        }

        cfg.Apps.Add(new AppEntry
        {
            Name = d.Name,
            Folder = d.Folder,
            VersionAgnostic = d.VersionAgnostic,
            SingleFile = d.SingleFile,
            Launch = File.Exists(path) ? path : null,
        });
        Console.WriteLine("  " + Cli.S(cfg, "added_name", d.Name));
        Console.WriteLine("  " + d.Folder);
        return true;
    }

    /// <summary>
    /// Включение защиты — это автозапуск: служба стартует с нужными правами сама,
    /// иначе человеку пришлось бы держать открытым терминал с «sudo chp daemon».
    /// </summary>
    private static void AskProtection(CehoConfig cfg)
    {
        if (!Interactive) return;
        if (!Cli.AskYes("  " + Cli.S(cfg, "ask_protect"), true)) return;

        if (!Os.IsElevated())
        {
            Console.WriteLine("  " + Cli.S(cfg, "ask_protect_root", Os.IsWindows ? "" : "sudo "));
            return;
        }

        var err = Autostart.Enable(Ceho.OwnExecutablePath, Ceho.Root);
        if (err is not null) { Console.WriteLine("  " + err); return; }
        Autostart.Restart();
        Console.WriteLine("  " + Cli.S(cfg, "autostart_state_on"));
        Console.WriteLine("  " + Strings.T(cfg.Language, "panel_at", $"http://127.0.0.1:{cfg.WebPort}"));
    }

    // ── выбор из списка для команд без аргумента ──────────────────────

    /// <summary>Номер строки или пусто. Возвращает −1, если человек ничего не выбрал.</summary>
    public static int Pick(CehoConfig cfg, IReadOnlyList<string> rows, string title)
    {
        if (rows.Count == 0) { Console.WriteLine("  " + Cli.S(cfg, "ask_nothing_to_pick")); return -1; }

        Console.WriteLine("  " + title);
        for (var i = 0; i < rows.Count; i++) Console.WriteLine($"    {i + 1}. {rows[i]}");

        var answer = Cli.Ask("  " + Cli.S(cfg, "ask_choose") + " (" + Cli.S(cfg, "ask_skip") + ")");
        if (answer.Length == 0) return -1;
        if (int.TryParse(answer, out var n) && n >= 1 && n <= rows.Count) return n - 1;

        Console.WriteLine("  " + Cli.S(cfg, "ask_bad_choice"));
        return -1;
    }
}

using ProxyCage.Core;
using ProxyCage.Cli;

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch { }

CehoConfig cfg0;
try { cfg0 = CehoConfig.Load(Ceho.ConfigPath); }
catch { cfg0 = new CehoConfig(); }

Log.Init(Ceho.Root, args.Length > 0 ? args[0] : "chp");

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var ex = e.ExceptionObject as Exception;
    Log.Crash("необработанная ошибка", ex);

    Console.Error.WriteLine(ex is UnauthorizedAccessException
        ? Strings.T(cfg0.Language, "no_write_access", Ceho.ConfigPath,
            Os.IsWindows ? "" : "sudo ")
        : ex?.Message ?? "непредвиденная ошибка");
    Console.Error.WriteLine(Strings.T(cfg0.Language, "crash_written"));
    Environment.Exit(1);
};

// Упавшая фоновая задача роняла демон молча: причина не попадала ни в лог, ни на экран.
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Crash("фоновая задача", e.Exception);
    e.SetObserved();
};

if (args.Length == 0)
{
    if (!Assistant.Interactive) { Cli.PrintHelp(cfg0); return 0; }
    return await Assistant.RunAsync();
}

var cmd = args[0];

if (cmd is "help" or "--help" or "-h" or "/?")
{
    Cli.PrintHelp(cfg0);
    return 0;
}

if (cmd == "install")
{
    if (!Os.IsElevated())
    {
        Console.Error.WriteLine(Strings.T(cfg0.Language, "inst_need_rights",
            Strings.T(cfg0.Language, Os.IsWindows ? "pf_rights_fix_win" : "pf_rights_fix_unix")));
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_title"));
    Console.WriteLine();

    // Ставим ВМЕСТО прошлой версии, а не рядом с ней: иначе на машине живут два
    // экземпляра, и непонятно, чей автозапуск сработал.
    var previous = Installer.PrepareForNewVersion(
        Ceho.Root, m => Console.WriteLine("  " + m), cfg0.Language);

    string installed;
    try { installed = Installer.Install(Ceho.Root, m => Console.WriteLine("  " + m), cfg0.Language); }
    catch (Exception ex) { Console.Error.WriteLine("  " + ex.Message); return 1; }

    var mayAskAboutEngine = Assistant.Interactive && !args.Contains("--no-setup");

    if (Os.ResolveSingBox(Ceho.Root) is null
        && (args.Contains("--with-engine")
            || (mayAskAboutEngine && Cli.AskYes(Strings.T(cfg0.Language, "inst_engine_ask"), true))))
    {
        try { await Installer.DownloadEngineAsync(Ceho.Root, m => Console.WriteLine("  " + m), cfg0.Language); }
        catch (Exception ex)
        {
            Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_engine_failed", ex.Message));
            Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_engine_skip"));
        }
    }

    Console.WriteLine();
    Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_done"));
    Console.WriteLine("  " + Strings.T(cfg0.Language, "product_page_at", Brand.RepoUrl(cfg0.UpdateRepo)));

    // Обновление не должно оставлять машину без защиты: что работало — включаем обратно.
    if (previous.AutostartWasOn)
    {
        var err = Autostart.Enable(Installer.BinaryPath(Ceho.Root), Ceho.Root);
        Console.WriteLine("  " + (err ?? Strings.T(cfg0.Language, "inst_autostart_back")));
        if (err is null) Autostart.Restart();
    }
    else if (previous.WasRunning)
    {
        Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_start_again",
            Os.IsWindows ? "" : "sudo "));
    }

    if (!args.Contains("--no-setup")) return await Cli.SetupAsync(Ceho.ConfigPath);

    if (Os.ResolveSingBox(Ceho.Root) is null)
    {
        Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_engine_later"));
        Console.WriteLine("  " + Strings.T(cfg0.Language, "engine_command",
            Os.IsWindows ? "" : "sudo "));
    }
    Console.WriteLine("  " + Strings.T(cfg0.Language, "setup_hint"));
    return 0;
}

// Отдельная короткая команда: без неё «поставьте движок» упиралось в chp install
// с ключом, а это выглядит как переустановка программы.
if (cmd is "engine" or "движок")
{
    var already = Os.ResolveSingBox(Ceho.Root);
    var again = args.Length >= 2 && args[1].ToLowerInvariant() is "update" or "обновить" or "--force";

    if (already is not null && !again)
    {
        Console.WriteLine(Strings.T(cfg0.Language, "engine_already", already));
        Console.WriteLine(Strings.T(cfg0.Language, "engine_update_hint", Os.IsWindows ? "" : "sudo "));
        return 0;
    }

    if (!Preflight.FolderIsWritable(Ceho.Root, out _))
    {
        Console.Error.WriteLine(Strings.T(cfg0.Language, "engine_need_rights",
            Os.IsWindows ? "" : "sudo "));
        return 1;
    }

    try
    {
        var engine = await Installer.DownloadEngineAsync(
            Ceho.Root, m => Console.WriteLine("  " + m), cfg0.Language);
        Console.WriteLine(Strings.T(cfg0.Language, "engine_ready", engine));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(Strings.T(cfg0.Language, "inst_engine_failed", ex.Message));
        Console.Error.WriteLine(Strings.T(cfg0.Language, "engine_by_hand", Ceho.Root));
        return 1;
    }
    return 0;
}

if (cmd == "setup") return await Cli.SetupAsync(Ceho.ConfigPath);

if (cmd == "version")
{
    Console.WriteLine(Updater.CurrentVersion);
    Console.WriteLine(Brand.RepoUrl(cfg0.UpdateRepo));
    return 0;
}

if (cmd is "wrap" or "unwrap" or "wrapped")
{
    if (cmd == "wrapped")
    {
        var list = Cli.Wrapped();
        Console.WriteLine(Strings.T(cfg0.Language, "wrap_list"));
        Console.WriteLine();
        if (list.Count == 0) Console.WriteLine("  " + Strings.T(cfg0.Language, "wrap_empty"));
        else foreach (var w in list) Console.WriteLine("  " + w);
        return 0;
    }

    var name = args.Length >= 2 ? args[1] : null;
    if (name is null)
    {
        if (!Assistant.Interactive)
        { Console.Error.WriteLine(Strings.T(cfg0.Language, "wrap_need_name")); return 1; }

        var choices = cmd == "unwrap"
            ? Cli.Wrapped().ToList()
            : AiTools.Detect().Where(t => t.Kind == AiTools.ToolKind.Script)
                .Select(t => Path.GetFileName(t.Path)).Distinct()
                .Where(n => !Cli.Wrapped().Contains(n)).ToList();

        var picked = Assistant.Pick(cfg0, choices,
            Strings.T(cfg0.Language, cmd == "unwrap" ? "wrap_list" : "run_title"));
        if (picked < 0) return 0;
        name = choices[picked];
    }

    if (cmd == "unwrap")
    {
        var e = Cli.Unwrap(name);
        if (e is not null) { Console.Error.WriteLine(e); return 1; }
        Console.WriteLine(Strings.T(cfg0.Language, "unwrap_done", name));
        return 0;
    }

    var err = Cli.Wrap(name, out var note);
    if (err is not null) { Console.Error.WriteLine(Strings.T(cfg0.Language, "wrap_failed", err)); return 1; }
    Console.WriteLine(Strings.T(cfg0.Language, "wrap_done", name));
    if (note is not null) Console.WriteLine(note);
    Console.WriteLine(Strings.T(cfg0.Language, "wrap_hint", name));
    return 0;
}

if (cmd == "run")
{
    var rest = args.Skip(1).ToArray();
    if (rest.Length == 0) { Console.Error.WriteLine(Strings.T(cfg0.Language, "run_need_cmd")); return 1; }

    var proxyPort = Auth.ReadProxyPointer(Ceho.Root) ?? cfg0.MixedPort;
    if (!NodeProbe.TunnelIsUp(cfg0.TunAddress) && !DaemonControl.IsRunning(Ceho.Root))
    {
        Console.Error.WriteLine(Strings.T(cfg0.Language, "run_not_on", Os.IsWindows ? "" : "sudo "));
        return 1;
    }
    return Cli.RunThroughTunnel(proxyPort, rest);
}

if (Cli.ConfigUnreadable(Ceho.ConfigPath) ||
    (Cli.ChangesSettings(cmd) && Cli.ConfigReadOnly(Ceho.ConfigPath)))
{
    if (!Cli.CanRunRemotely(cmd))
    {
        Console.Error.WriteLine(Strings.T(cfg0.Language, "remote_not_allowed", cmd));
        return 1;
    }
    return await Cli.RunRemoteAsync(Ceho.Root, args, cfg0.Language);
}

if (!Cli.Allowed(cfg0, args, out var authError))
{
    Console.Error.WriteLine(authError);
    return 1;
}

switch (cmd)
{
    case "add-app":
    {
        if (args.Length < 2)
        {
            if (!Assistant.Interactive) { Console.Error.WriteLine(Cli.S(cfg0, "err_need_path")); return 1; }
            var c = CehoConfig.Load(Ceho.ConfigPath);
            if (!Assistant.AskApps(c)) return 0;
            c.Save(Ceho.ConfigPath);
            Auth.RestrictConfigAccess(Ceho.ConfigPath);
            try { Console.WriteLine(await Ceho.ApplyAsync()); } catch (Exception ex) { Console.WriteLine(ex.Message); }
            return 0;
        }
        var raw = args[1];
        if (!File.Exists(raw) && !Directory.Exists(raw))
        {
            Console.Error.WriteLine(Cli.S(cfg0, "err_no_such_path", raw));
            return 1;
        }

        AppDetector.Detection d;
        try { d = AppDetector.Detect(raw, cfg0.Language); }
        catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 1; }

        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (cfg.Apps.Any(a => a.Folder.Equals(d.Folder, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine(Cli.S(cfg, "err_already_added"));
            return 1;
        }
        cfg.Apps.Add(new AppEntry
        {
            Name = d.Name, Folder = d.Folder,
            VersionAgnostic = d.VersionAgnostic,
            SingleFile = d.SingleFile,
            Launch = File.Exists(raw) ? raw : null,
        });
        cfg.Save(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, "added_name", d.Name));
        Console.WriteLine((d.SingleFile ? Cli.S(cfg, "col_file") : Cli.S(cfg, "col_folder")) + ": " + d.Folder);
        Console.WriteLine(d.Explanation);
        await Cli.RebuildQuietlyAsync(cfg);
        return 0;
    }

    case "apps":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (cfg.Apps.Count == 0) { Console.WriteLine(Cli.S(cfg, "empty")); return 0; }
        foreach (var a in cfg.Apps)
        {
            var how = a.AllowedNodes.Count == 0
                ? Cli.S(cfg, "app_tunnel_general")
                : Cli.S(cfg, "app_tunnel_pinned", a.AllowedNodes.Count);
            Console.WriteLine($"{a.Name,-24} {a.Folder}  · {how}");
        }
        return 0;
    }

    case "remove-app":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var which = args.Length >= 2 ? args[1] : null;
        if (which is null)
        {
            if (!Assistant.Interactive) { Console.Error.WriteLine(Cli.S(cfg, "err_need_path")); return 1; }
            var i = Assistant.Pick(cfg, cfg.Apps.Select(a => $"{a.Name,-20} {a.Folder}").ToList(),
                Cli.S(cfg, "apps_title"));
            if (i < 0) return 0;
            which = cfg.Apps[i].Folder;
        }

        var target = Os.RealPath(which);
        var n = cfg.Apps.RemoveAll(a =>
            a.Folder.Equals(which, StringComparison.OrdinalIgnoreCase) ||
            a.Folder.Equals(target, StringComparison.OrdinalIgnoreCase));
        cfg.Save(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, n > 0 ? "removed" : "err_not_in_list"));
        if (n > 0) await Cli.RebuildQuietlyAsync(cfg);
        return n > 0 ? 0 : 1;
    }

    case "tunnel":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (!Assistant.Interactive)
        {
            Console.Error.WriteLine(Cli.S(cfg, "tunnel_need_interactive"));
            return 1;
        }
        if (cfg.Apps.Count == 0) { Console.WriteLine(Cli.S(cfg, "empty")); return 0; }

        Console.WriteLine(Cli.S(cfg, "apps_title"));
        Console.WriteLine();
        for (var i = 0; i < cfg.Apps.Count; i++)
        {
            var a = cfg.Apps[i];
            var how = a.AllowedNodes.Count == 0
                ? Cli.S(cfg, "app_tunnel_general")
                : Cli.S(cfg, "app_tunnel_pinned", a.AllowedNodes.Count);
            Console.WriteLine($"  {i + 1,3}. {a.Name,-20} {how}");
        }

        var appsAnswer = Cli.Ask("  " + Cli.S(cfg, "tunnel_ask_apps") + " (" + Cli.S(cfg, "ask_skip") + ")");
        if (appsAnswer.Length == 0) return 0;
        if (!IndexList.TryParse(appsAnswer, cfg.Apps.Count, out var appIdx) || appIdx.Count == 0)
        {
            Console.Error.WriteLine(Cli.S(cfg, "ask_bad_choice"));
            return 1;
        }

        List<ProxyNode> all;
        using (var spinner = new ConsoleSpinner(Cli.S(cfg, "sub_parsing_nodes")))
        {
            var loaded = await Ceho.LoadAllNodesAsync(cfg, preferCache: true, spinner.AsReport());
            all = Cli.NodeList(loaded);
            spinner.Done(Cli.S(cfg, "nodes_read", all.Count));
        }
        if (all.Count == 0) { Console.Error.WriteLine(Cli.S(cfg, "pf_no_subs_detail")); return 1; }

        Console.WriteLine();
        for (var i = 0; i < all.Count; i++)
        {
            var n = all[i];
            var name = n.Remark.Length > 0 ? n.Remark : n.Tag;
            Console.WriteLine($"  {i + 1,3}. {(n.CountryCode ?? "?"),-3} {n.Server}:{n.Port,-5}  {name}");
        }

        var nodesAnswer = Cli.Ask("  " + Cli.S(cfg, "tunnel_ask_nodes"));
        List<string> keys;
        if (string.IsNullOrWhiteSpace(nodesAnswer))
        {
            keys = new List<string>();
        }
        else if (!IndexList.TryParse(nodesAnswer, all.Count, out var nodeIdx) || nodeIdx.Count == 0)
        {
            Console.Error.WriteLine(Cli.S(cfg, "ask_bad_choice"));
            return 1;
        }
        else
        {
            keys = nodeIdx.Select(i => all[i].Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var names = new List<string>();
        foreach (var i in appIdx)
        {
            cfg.Apps[i].AllowedNodes = keys.ToList();
            names.Add(cfg.Apps[i].Name);
        }

        cfg.Save(Ceho.ConfigPath);
        Auth.RestrictConfigAccess(Ceho.ConfigPath);

        var who = string.Join(", ", names);
        Console.WriteLine(keys.Count == 0
            ? Cli.S(cfg, "tunnel_cleared", who)
            : Cli.S(cfg, "tunnel_done", who, keys.Count));
        await Cli.RebuildQuietlyAsync(cfg);
        return 0;
    }

    case "sub-add":
    {
        if (args.Length < 3)
        {
            if (!Assistant.Interactive) { Console.Error.WriteLine(Cli.S(cfg0, "err_need_sub_args")); return 1; }
            var c = CehoConfig.Load(Ceho.ConfigPath);
            if (!await Assistant.AskSubscriptionAsync(c)) return 0;
            c.Save(Ceho.ConfigPath);
            Auth.RestrictConfigAccess(Ceho.ConfigPath);
            try { Console.WriteLine(await Ceho.ApplyAsync()); } catch (Exception ex) { Console.WriteLine(ex.Message); }
            return 0;
        }
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (cfg.Subscriptions.Any(s => s.Name == args[1]))
        {
            Console.Error.WriteLine(Cli.S(cfg, "err_sub_exists", args[1]));
            return 1;
        }
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = args[1], Url = args[2] });
        cfg.ActiveSubscription ??= args[1];
        cfg.Save(Ceho.ConfigPath);
        Auth.RestrictConfigAccess(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, "sub_added", args[1]));
        Console.WriteLine(Cli.S(cfg, "subs_pool"));
        using (var spinner = new ConsoleSpinner(Cli.S(cfg, "ask_sub_checking")))
        {
            try
            {
                var nodes = await Ceho.LoadAllNodesAsync(cfg, preferCache: false, spinner.AsReport());
                spinner.Done(Cli.S(cfg, "sub_parsed", nodes.Count));
                Console.WriteLine();
                Assistant.PrintPoolBreakdown(nodes, cfg);
            }
            catch (Exception ex)
            {
                spinner.Done(ex.Message);
            }
        }
        await Cli.RebuildQuietlyAsync(cfg);
        return 0;
    }

    case "subs":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (cfg.Subscriptions.Count == 0) { Console.WriteLine(Cli.S(cfg, "empty")); return 0; }
        foreach (var s in cfg.Subscriptions)
        {
            var state = s.LastCheckOk switch
            {
                true => Cli.S(cfg, "sub_ok"),
                false => Cli.S(cfg, "sub_bad"),
                null => Cli.S(cfg, "sub_unchecked"),
            };
            var until = s.ExpiresUtc is { } when
                ? $"{when.ToLocalTime():dd.MM.yyyy} ({SubscriptionInfo.DaysLeft(when)})"
                : "-";
            Console.WriteLine($"[{(s.Enabled ? "x" : " ")}] {s.Name,-16} {state,-14} {until,-18} {s.Url}");
            if (s.UsedBytes is { } used)
                Console.WriteLine($"      {Cli.S(cfg, "col_traffic")}: {SubscriptionInfo.Bytes(used)}" +
                                  (s.TotalBytes is { } total ? Cli.S(cfg, "sub_of_total", SubscriptionInfo.Bytes(total)) : ""));
            if (s.LastError is not null) Console.WriteLine($"      {s.LastError}");
        }
        Console.WriteLine();
        Console.WriteLine(Cli.S(cfg, "subs_pool"));
        Console.WriteLine("  chp sub-off ИМЯ · chp sub-on ИМЯ");
        return 0;
    }

    case "sub-on":
    case "sub-off":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var wanted = cmd == "sub-on";

        var name = args.Length >= 2 ? args[1] : null;
        if (name is null)
        {
            var choices = cfg.Subscriptions.Where(s => s.Enabled != wanted).ToList();
            if (!Assistant.Interactive || choices.Count == 0)
            {
                Console.Error.WriteLine(Cli.S(cfg, "err_need_sub_name"));
                return 1;
            }
            var i = Assistant.Pick(cfg, choices.Select(s => $"{s.Name,-16} {s.Url}").ToList(),
                Cli.S(cfg, "nav_subs"));
            if (i < 0) return 0;
            name = choices[i].Name;
        }

        var entry = cfg.Subscriptions.FirstOrDefault(s => s.Name == name);
        if (entry is null) { Console.Error.WriteLine(Cli.S(cfg, "err_no_such_sub", name)); return 1; }

        if (!wanted && cfg.Subscriptions.Count(s => s.Enabled) <= 1 && entry.Enabled)
        {
            Console.Error.WriteLine(Cli.S(cfg, "subs_last_one"));
            return 1;
        }

        entry.Enabled = wanted;
        cfg.Save(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, wanted ? "sub_turned_on" : "sub_turned_off", name));
        await Cli.RebuildQuietlyAsync(cfg);
        return 0;
    }

    case "log":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);

        // Журнал один, аргументы только выбирают, что из него показать:
        // chp log · chp log 300 · chp log engine · chp log crash · chp log clear
        var view = LogView.All;
        var lines = 60;
        var clear = false;

        foreach (var arg in args.Skip(1))
        {
            if (int.TryParse(arg, out var n)) { lines = Math.Clamp(n, 1, 5000); continue; }
            switch (arg.ToLowerInvariant())
            {
                case "engine" or "движок": view = LogView.Engine; break;
                case "ours" or "наше": view = LogView.Ours; break;
                case "crash" or "crashes" or "падения": view = LogView.Crashes; break;
                case "all" or "всё" or "все": view = LogView.All; break;
                case "clear" or "очистить": clear = true; break;
                default:
                    Console.Error.WriteLine(Cli.S(cfg, "log_arg_bad", arg));
                    return 1;
            }
        }

        if (clear)
        {
            Log.Clear();
            Console.WriteLine(Cli.S(cfg, "log_cleared"));
            return 0;
        }

        if (view is LogView.All or LogView.Crashes)
        {
            var crashes = Log.Crashes();
            if (crashes.Count > 0)
            {
                Console.WriteLine(Cli.S(cfg, "log_crashes", crashes.Count));
                var last = crashes[0];
                Console.WriteLine(Cli.S(cfg, "log_crash_last", last.When.ToString("dd.MM HH:mm"), last.Context));
                foreach (var line in last.Lines.Take(20)) Console.WriteLine("  " + line);
                Console.WriteLine();
            }
            else if (view == LogView.Crashes)
            {
                Console.WriteLine(Cli.S(cfg, "log_no_crashes"));
                return 0;
            }
        }

        var title = view switch
        {
            LogView.Engine => "log_view_engine",
            LogView.Ours => "log_view_ours",
            LogView.Crashes => "log_view_crashes",
            _ => "log_view_all",
        };
        Console.WriteLine(Cli.S(cfg, title) + $"  ({Log.FilePath})");

        var tail = Log.Tail(lines, view);
        if (tail.Count == 0) Console.WriteLine("  " + Cli.S(cfg, "log_empty"));
        else foreach (var line in tail) Console.WriteLine("  " + line);

        if (view == LogView.All) Console.WriteLine(Cli.S(cfg, "log_cli_hint"));
        return 0;
    }

    case "sub-remove":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var name = args.Length >= 2 ? args[1] : null;
        if (name is null)
        {
            if (!Assistant.Interactive) { Console.Error.WriteLine(Cli.S(cfg, "err_need_sub_name")); return 1; }
            var i = Assistant.Pick(cfg, cfg.Subscriptions.Select(s => $"{s.Name,-16} {s.Url}").ToList(),
                Cli.S(cfg, "nav_subs"));
            if (i < 0) return 0;
            name = cfg.Subscriptions[i].Name;
        }

        var n = cfg.Subscriptions.RemoveAll(s => s.Name == name);
        if (cfg.ActiveSubscription == name)
            cfg.ActiveSubscription = cfg.Subscriptions.FirstOrDefault()?.Name;
        cfg.Save(Ceho.ConfigPath);
        try
        {
            var cache = Path.Combine(Ceho.Root, $"sub-{name}.txt");
            if (File.Exists(cache)) File.Delete(cache);
        }
        catch { }
        Console.WriteLine(Cli.S(cfg, n > 0 ? "sub_removed" : "err_no_such_sub", name));
        if (n > 0 && cfg.Subscriptions.Count > 0) await Cli.RebuildQuietlyAsync(cfg);
        return n > 0 ? 0 : 1;
    }

    case "countries":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        IReadOnlyList<ProxyNode> nodes;
        try
        {
            using var spinner = new ConsoleSpinner(Cli.S(cfg, "job_pool"));
            nodes = await Ceho.LoadAllNodesAsync(cfg, preferCache: false, spinner.AsReport());
            spinner.Done(Cli.S(cfg, "pool_loaded", nodes.Count));
        }
        catch (Exception ex) { Cli.Stuck(cfg, ex.Message); return 1; }

        Cli.PrintCountries(cfg, nodes);
        Console.WriteLine();
        Console.WriteLine("  " + Cli.S(cfg, "countries_hint"));
        Console.WriteLine("  chp country off RU · chp country on DE · chp country only NL");
        return 0;
    }

    case "country":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);

        if (args.Length < 2)
        {
            if (!Assistant.Interactive) { Console.Error.WriteLine(Cli.S(cfg, "err_country_usage")); return 1; }
            return await Cli.CountryMenuAsync(cfg);
        }

        var prevExcluded = new List<string>(cfg.ExcludedCountries);
        var prevPreferred = new List<string>(cfg.PreferredCountries);

        var sub = args[1].ToLowerInvariant();
        if (sub == "any")
        {
            cfg.PreferredCountries.Clear();
            cfg.Save(Ceho.ConfigPath);
            Console.WriteLine(Cli.S(cfg, "country_all_allowed"));
        }
        else if (sub is "on" or "off" or "only" && args.Length >= 3)
        {
            var code = args[2].ToUpperInvariant();
            switch (sub)
            {
                case "on":
                    cfg.ExcludedCountries.RemoveAll(c => c.Equals(code, StringComparison.OrdinalIgnoreCase));
                    Console.WriteLine(Cli.S(cfg, "country_on", code));
                    break;
                case "off":
                    if (!cfg.ExcludedCountries.Contains(code, StringComparer.OrdinalIgnoreCase))
                        cfg.ExcludedCountries.Add(code);
                    cfg.PreferredCountries.RemoveAll(c => c.Equals(code, StringComparison.OrdinalIgnoreCase));
                    Console.WriteLine(Cli.S(cfg, "country_off", code));
                    break;
                case "only":
                    cfg.PreferredCountries = new List<string> { code };
                    cfg.ExcludedCountries.RemoveAll(c => c.Equals(code, StringComparison.OrdinalIgnoreCase));
                    Console.WriteLine(Cli.S(cfg, "country_only", code));
                    break;
            }
            cfg.Save(Ceho.ConfigPath);
        }
        else
        {
            Console.Error.WriteLine(Cli.S(cfg, "err_country_usage"));
            return 1;
        }

        try { Console.WriteLine(await Ceho.ApplyAsync()); }
        catch (PoolEmptyException ex)
        {
            cfg.ExcludedCountries = prevExcluded;
            cfg.PreferredCountries = prevPreferred;
            cfg.Save(Ceho.ConfigPath);
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Cli.S(cfg, "change_reverted"));
            return 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        return 0;
    }

    case "passwd":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (args.Contains("--clear"))
        {
            Auth.ClearPassword(cfg);
            cfg.Save(Ceho.ConfigPath);
            Auth.DropAllSessions();
            Console.WriteLine(Cli.S(cfg, "auth_cleared"));
            Console.WriteLine(Cli.S(cfg, "auth_no_password"));
            return 0;
        }

        var pass = Cli.Opt(args, "--new") ?? Cli.AskSecret(Cli.S(cfg, "auth_password"));
        if (pass.Length < 4) { Console.Error.WriteLine(Cli.S(cfg, "setup_password_short")); return 1; }
        if (Cli.Opt(args, "--new") is null)
        {
            var again = Cli.AskSecret(Cli.S(cfg, "setup_password_again"));
            if (pass != again) { Console.Error.WriteLine(Cli.S(cfg, "setup_password_mismatch")); return 1; }
        }
        Auth.SetPassword(cfg, pass);
        cfg.Save(Ceho.ConfigPath);
        Auth.RestrictConfigAccess(Ceho.ConfigPath);
        Auth.DropAllSessions();
        Console.WriteLine(Cli.S(cfg, "auth_set_ok"));
        return 0;
    }

    case "lang":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (args.Length < 2 && !Assistant.Interactive)
        {
            Console.WriteLine(cfg.Language);
            return 0;
        }
        cfg.Language = Strings.Normalize(args.Length >= 2
            ? args[1]
            : Cli.Ask("  " + Cli.S(cfg, "ask_lang") + " (ru/en)", cfg.Language));
        cfg.Save(Ceho.ConfigPath);
        Console.WriteLine(Strings.T(cfg.Language, "lang_set", cfg.Language));
        return 0;
    }

    case "update":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, "upd_current", Updater.CurrentVersion));
        try
        {
            var release = await Updater.CheckAsync(Cli.Opt(args, "--repo") ?? cfg.UpdateRepo);
            if (release is null) { Console.WriteLine(Cli.S(cfg, "upd_none")); return 0; }

            Console.WriteLine(Cli.S(cfg, "upd_found", release.Version));
            if (!args.Contains("--yes") && !Cli.AskYes(Cli.S(cfg, "upd_apply"), true))
            { Console.WriteLine(Cli.S(cfg, "cancelled")); return 0; }

            Console.WriteLine("  " + Cli.S(cfg, "upd_stopping_tun"));
            var shut = TunnelShutdown.PrepareForUpdate(cfg, Ceho.Root, Ceho.RuntimeConfigPath, Console.WriteLine);
            if (!shut.Ok)
            {
                Console.Error.WriteLine(Cli.S(cfg, shut.ErrorKey ?? "upd_need_reboot"));
                return 1;
            }

            await Updater.InstallAsync(release, Ceho.OwnExecutablePath, Console.WriteLine);
            Cli.MakeShortcut(Ceho.OwnExecutablePath, out _);

            try
            {
                Console.WriteLine("  " + await Ceho.ApplyAsync());
            }
            catch (Exception ex)
            {
                Console.WriteLine("  " + ex.Message);
            }

            if (Autostart.IsEnabled()) Autostart.Restart();
            Console.WriteLine(Cli.S(cfg, "upd_done", release.Version));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Cli.S(cfg, "upd_failed", ex.Message));
            return 1;
        }
    }

    case "alias":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var err = Cli.MakeShortcut(Ceho.OwnExecutablePath, out var made);
        Console.WriteLine(err is null
            ? Cli.S(cfg, "alias_made", made ?? "chp")
            : Cli.S(cfg, "alias_failed", err));
        return err is null ? 0 : 1;
    }

    case "detect":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var found = AiTools.Detect();
        Console.WriteLine(Cli.S(cfg, "ai_title"));
        Console.WriteLine();
        if (found.Count == 0)
        {
            Console.WriteLine("  " + Cli.S(cfg, "ai_none"));
            return 0;
        }
        foreach (var t in found)
        {
            var mark = AppCoverage.IsToolCovered(cfg, t) ? "[x]" : "[ ]";
            Console.WriteLine($"  {mark} {t.Name,-12} {t.Path}");
            if (t.Kind == AiTools.ToolKind.Script && t.Interpreter is not null)
                Console.WriteLine("      " + Cli.S(cfg, "ai_script_warn", Path.GetFileName(t.Interpreter)));
        }
        Console.WriteLine();
        Console.WriteLine("  chp add-app ПУТЬ");
        return 0;
    }

    case "browser":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, "browser_title"));
        Console.WriteLine();
        Console.WriteLine("  " + Cli.S(cfg, "browser_lede"));
        Console.WriteLine();
        Console.WriteLine($"  SOCKS5 : 127.0.0.1:{cfg.MixedPort}");
        Console.WriteLine($"  HTTP   : 127.0.0.1:{cfg.MixedPort}");
        Console.WriteLine();
        Console.WriteLine("  " + Cli.S(cfg, "browser_howto", cfg.MixedPort));
        Console.WriteLine("  " + Cli.S(cfg, "browser_note"));
        return 0;
    }

    case "set-port":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var portText = args.Length >= 2 ? args[1]
            : Assistant.Interactive ? Cli.Ask("  " + Cli.S(cfg, "ask_port"), cfg.WebPort.ToString()) : "";
        if (!int.TryParse(portText, out var wp) || wp is < 1 or > 65535)
        { Console.Error.WriteLine(Cli.S(cfg, "err_need_port")); return 1; }
        cfg.WebPort = wp;
        cfg.Save(Ceho.ConfigPath);
        Console.WriteLine(Strings.T(cfg.Language, "panel_at", $"http://127.0.0.1:{wp}"));
        return 0;
    }

    case "set-proxy-port":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var portText = args.Length >= 2 ? args[1]
            : Assistant.Interactive ? Cli.Ask("  " + Cli.S(cfg, "ask_proxy_port"), cfg.MixedPort.ToString()) : "";
        if (!int.TryParse(portText, out var mp) || mp is < 1 or > 65535)
        { Console.Error.WriteLine(Cli.S(cfg, "err_need_port")); return 1; }
        cfg.MixedPort = mp;
        cfg.Save(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, "proxy_port_set", mp));
        await Cli.RebuildQuietlyAsync(cfg);
        return 0;
    }

    case "timeout":
    case "set-timeout":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (args.Length < 2)
        {
            if (Assistant.Interactive)
            {
                var input = Cli.Ask("  " + Cli.S(cfg, "ask_timeout"), cfg.TimeoutSeconds.ToString());
                if (int.TryParse(input, out var t) && t is >= 1 and <= 300)
                {
                    cfg.TimeoutSeconds = t;
                    cfg.Save(Ceho.ConfigPath);
                    Console.WriteLine(Cli.S(cfg, "timeout_set", t));
                    return 0;
                }
                Console.Error.WriteLine(Cli.S(cfg, "err_need_timeout"));
                return 1;
            }
            Console.WriteLine(Cli.S(cfg, "timeout_current", cfg.TimeoutSeconds));
            return 0;
        }

        if (!int.TryParse(args[1], out var sec) || sec is < 1 or > 300)
        {
            Console.Error.WriteLine(Cli.S(cfg, "err_need_timeout"));
            return 1;
        }

        cfg.TimeoutSeconds = sec;
        cfg.Save(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, "timeout_set", sec));
        return 0;
    }

    case "nodes":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (NodeProbe.TunnelIsUp(cfg.TunAddress))
        {
            Console.Error.WriteLine(Cli.S(cfg, "measure_blocked"));
            return 1;
        }
        var nodes = await Ceho.LoadAllNodesAsync(cfg);
        var rows = await NodeProbe.ByCountryAsync(nodes);

        var filter = args.Length > 1 ? args[1].ToUpperInvariant() : null;
        foreach (var r in rows)
        {
            if (filter is not null && !string.Equals(r.Code, filter, StringComparison.OrdinalIgnoreCase)) continue;
            var best = r.BestMs is null ? Cli.S(cfg, "not_measured") : $"{r.BestMs} ms";
            var on = Cli.CountryEnabled(cfg, r.Code) ? "x" : " ";
            var name = r.Name ?? Cli.S(cfg, "country_unknown");
            Console.WriteLine($"[{on}] {Cli.FlagCell(r.Code)} {r.Code,-3} {name,-22} {r.Nodes,3} / {r.Alive,-3} {best}");
            if (filter is not null)
                foreach (var m in r.Items)
                    Console.WriteLine($"      {m.Node.Protocol,-12} {m.Node.Server}:{m.Node.Port,-6} " +
                                      $"{(m.LatencyMs is null ? "-" : m.LatencyMs + " ms"),-9} {m.Node.Source} · {m.Node.Remark}");
        }
        Console.WriteLine();
        Console.WriteLine(Cli.S(cfg, "udp_not_measured"));
        return 0;
    }

    case "node":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);

        var action = args.Length >= 2 ? args[1].ToLowerInvariant() : "";
        if (action is not ("" or "on" or "off"))
        {
            Console.Error.WriteLine(Cli.S(cfg, "node_usage"));
            return 1;
        }

        List<ProxyNode> all;
        using (var spinner = new ConsoleSpinner(Cli.S(cfg, "sub_parsing_nodes")))
        {
            var loaded = await Ceho.LoadAllNodesAsync(cfg, preferCache: true, spinner.AsReport());
            all = Cli.NodeList(loaded);
            spinner.Done(Cli.S(cfg, "nodes_read", all.Count));
        }

        if (all.Count == 0) { Console.Error.WriteLine(Cli.S(cfg, "pf_no_subs_detail")); return 1; }

        if (action.Length == 0)
        {
            Cli.PrintNodes(cfg, all);
            return 0;
        }

        var what = args.Length >= 3 ? args[2] : null;
        if (what is null) { Console.Error.WriteLine(Cli.S(cfg, "node_usage")); return 1; }

        var prevBlocked = new List<string>(cfg.BlockedNodes);
        List<ProxyNode> touched;

        if (action == "on" && what.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            touched = all.Where(n => SingBoxConfigGenerator.IsBlockedByHand(n, cfg)).ToList();
            cfg.BlockedNodes.Clear();
        }
        else
        {
            var found = Cli.FindNodes(all, what);
            if (found.Count == 0)
            {
                Console.Error.WriteLine(Cli.S(cfg, "node_none_found", what));
                return 1;
            }

            var wanted = action == "on";
            touched = found
                .Where(n => SingBoxConfigGenerator.IsBlockedByHand(n, cfg) == wanted)
                .ToList();

            foreach (var n in touched)
                if (wanted) cfg.BlockedNodes.RemoveAll(k => k.Equals(n.Key, StringComparison.OrdinalIgnoreCase));
                else cfg.BlockedNodes.Add(n.Key);
        }

        if (touched.Count == 0)
        {
            Console.WriteLine(Cli.S(cfg, "nodes_unchanged"));
            return 0;
        }

        cfg.Save(Ceho.ConfigPath);

        // Сначала пересборка правил, и только потом отчёт: иначе при откате
        // на экране остаётся «выключено», хотя ничего не выключилось.
        string applied;
        try { applied = await Ceho.ApplyAsync(); }
        catch (PoolEmptyException ex)
        {
            cfg.BlockedNodes = prevBlocked;
            cfg.Save(Ceho.ConfigPath);
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Cli.S(cfg, "change_reverted"));
            return 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

        foreach (var n in touched) Console.WriteLine("  " + Cli.NodeLine(cfg, n));
        Console.WriteLine(Cli.S(cfg, action == "on" ? "node_turned_on" : "node_turned_off", touched.Count));
        Console.WriteLine(applied);
        return 0;
    }

    case "speed":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);

        if (args.Length >= 2 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            cfg.MaxLatencyMs = null;
            cfg.Save(Ceho.ConfigPath);
            Console.WriteLine(Cli.S(cfg, "speed_off"));
            await Cli.RebuildQuietlyAsync(cfg);
            return 0;
        }

        if (NodeProbe.TunnelIsUp(cfg.TunAddress))
        {
            Console.Error.WriteLine(Cli.S(cfg, "measure_blocked"));
            return 1;
        }

        var limitText = args.Length >= 2 ? args[1]
            : Assistant.Interactive ? Cli.Ask("  " + Cli.S(cfg, "speed_limit"),
                (cfg.MaxLatencyMs ?? 500).ToString())
            : "";
        if (!int.TryParse(limitText, out var limit) || limit <= 0)
        { Console.Error.WriteLine(Cli.S(cfg, "speed_usage")); return 1; }

        var previousLimit = cfg.MaxLatencyMs;
        var left = await Cli.MeasureAndFilterAsync(cfg, limit);

        if (left is null) { cfg.MaxLatencyMs = previousLimit; return 1; }

        if (left == 0)
        {
            cfg.MaxLatencyMs = previousLimit;
            Console.Error.WriteLine(Cli.S(cfg, "speed_none_left", limit));
            Console.Error.WriteLine(Cli.S(cfg, "change_reverted"));
            return 1;
        }

        cfg.Save(Ceho.ConfigPath);
        await Cli.RebuildQuietlyAsync(cfg);
        return 0;
    }

    case "autostart":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (args.Length < 2)
        {
            Console.WriteLine(Cli.S(cfg, Autostart.IsEnabled() ? "autostart_state_on" : "autostart_state_off"));
            return 0;
        }
        if (args[1] == "on")
        {
            var err = Autostart.Enable(Ceho.OwnExecutablePath, Ceho.Root);
            Console.WriteLine(err ?? Cli.S(cfg, "autostart_state_on"));
            return err is null ? 0 : 1;
        }
        if (args[1] == "off")
        {
            var err = Autostart.Disable();
            Console.WriteLine(err ?? Cli.S(cfg, "autostart_state_off"));
            return err is null ? 0 : 1;
        }
        Console.Error.WriteLine("autostart on | autostart off");
        return 1;
    }

    case "uninstall":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (!args.Contains("--yes"))
        {
            if (Assistant.Interactive && Cli.AskYes("  " + Cli.S(cfg, "uninstall_confirm_ask"), false))
            {
                // proceed
            }
            else
            {
                Console.WriteLine(Cli.S(cfg, "uninstall_warn"));
                Console.WriteLine($"  {Ceho.Root}");
                Console.WriteLine(Cli.S(cfg, "uninstall_confirm"));
                return 1;
            }
        }

        Autostart.Purge();
        if (DaemonControl.RequestStop(Ceho.Root))
            await Task.Delay(TimeSpan.FromSeconds(8));

        TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Console.WriteLine);
        TunCleanup.KillOurProcesses(Installer.BinaryPath(Ceho.Root) + " daemon", Console.WriteLine);

        TunCleanup.RemoveLeftovers(Console.WriteLine, cfg.TunAddress, Ceho.Root);
        DaemonControl.ClearRunning(Ceho.Root);

        foreach (var f in new[] { Ceho.ConfigPath, Ceho.RuntimeConfigPath })
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        try
        {
            foreach (var f in Directory.GetFiles(Ceho.Root, "sub-*.txt")) File.Delete(f);
            foreach (var f in Directory.GetFiles(Ceho.Root, "*.log")) File.Delete(f);

            // Резервная копия прошлой версии больше ни для чего не нужна, а сообщение
            // об удалении обещает, что рядом остался только сам файл программы.
            foreach (var f in Directory.GetFiles(Ceho.Root, "*.old")) File.Delete(f);

            var pointer = Path.Combine(Ceho.Root, "panel.port");
            if (File.Exists(pointer)) File.Delete(pointer);
        }
        catch { }

        Installer.Remove(Ceho.Root, Console.WriteLine, cfg.Language);

        foreach (var name in new[] { Os.EngineFileName, Os.SingBoxFileName }.Distinct())
        {
            var ourEngine = Path.Combine(Ceho.Root, name);
            if (File.Exists(ourEngine))
                try { File.Delete(ourEngine); Console.WriteLine($"удалён движок: {ourEngine}"); }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
        }

        Console.WriteLine(Cli.S(cfg, "uninstall_done"));

        if (Os.IsWindows)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{Ceho.Root}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }
        else if (args.Contains("--purge"))
        {
            try
            {
                var self = Installer.BinaryPath(Ceho.Root);
                if (File.Exists(self)) File.Delete(self);
                if (Directory.Exists(Ceho.Root) && Directory.GetFileSystemEntries(Ceho.Root).Length == 0)
                    Directory.Delete(Ceho.Root);
                Console.WriteLine(Cli.S(cfg, "inst_removed"));
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
        }
        return 0;
    }

    case "apply":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        try
        {
            using var spinner = new ConsoleSpinner(Cli.S(cfg, "job_apply"));
            var done = await Ceho.ApplyAsync(spinner.AsReport());
            spinner.Done(done);
            return 0;
        }
        catch (Exception ex) { Cli.Stuck(cfg, ex.Message); return 1; }
    }

    case "status":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);

        var daemon = DaemonControl.IsRunning(Ceho.Root);
        var tunnel = NodeProbe.TunnelIsUp(cfg.TunAddress);
        var running = daemon && tunnel;
        Console.WriteLine(running ? Cli.S(cfg, "state_on")
            : daemon ? Cli.S(cfg, "state_broken")
            : Cli.S(cfg, "state_off"));

        if (!daemon && tunnel)
            Console.WriteLine(Cli.S(cfg, "state_leftovers", Os.IsWindows ? "" : "sudo "));
        Console.WriteLine(Cli.S(cfg, "apps_isolated", cfg.Apps.Count(a => a.Enabled)));
        Console.WriteLine(Cli.S(cfg, "subs_count", cfg.Subscriptions.Count));
        Console.WriteLine(Cli.S(cfg, "countries_title") + ": " +
            (cfg.PreferredCountries.Count > 0
                ? string.Join(", ", cfg.PreferredCountries)
                : cfg.ExcludedCountries.Count > 0
                    ? Cli.S(cfg, "country_any_but", string.Join(", ", cfg.ExcludedCountries))
                    : Cli.S(cfg, "country_any")));
        if (cfg.BlockedNodes.Count > 0)
            Console.WriteLine(Cli.S(cfg, "nodes_off_now", cfg.BlockedNodes.Count));
        if (!Auth.HasPassword(cfg)) Console.WriteLine(Cli.S(cfg, "auth_no_password"));
        if (running)
        {
            var (country, ip) = await Ceho.ProbeExitAsync(cfg.MixedPort);
            Console.WriteLine(ip is null
                ? Cli.S(cfg, "state_no_exit")
                : Strings.T(cfg.Language, "exit_is", country ?? "?", ip));
        }
        return 0;
    }

    case "doctor":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var tools = Cli.DoctorTools();

        var wantFix = args.Length >= 2 && args[1] is "fix" or "--fix" or "heal" or "--heal";
        var quiet = args.Contains("--yes") || !Assistant.Interactive;

        Doctor.Result report;
        using (var spinner = new ConsoleSpinner(Cli.S(cfg, wantFix ? "job_heal" : "job_doctor")))
        {
            report = wantFix
                ? await Doctor.HealAsync(cfg, Ceho.ConfigPath, Ceho.Root, tools, spinner.AsReport())
                : await Doctor.CheckAsync(cfg, Ceho.Root, tools, spinner.AsReport());
            spinner.Done(wantFix
                ? Doctor.Say(report, cfg.Language)
                : Doctor.Headline(report, cfg.Language));
        }

        Console.WriteLine();
        Cli.PrintChecks(cfg, report.Checks);

        Cli.PrintDoctorDeeds(cfg, report);

        // Осмотр сам ничего не меняет: чинить — только по слову хозяина или по «doctor fix».
        if (!wantFix && report.Fixable)
        {
            Console.WriteLine();
            if (quiet)
            {
                Console.WriteLine(Cli.S(cfg, "doc_offer", Os.IsWindows ? "" : "sudo "));
            }
            else if (Cli.AskYes(Cli.S(cfg, "doc_ask_fix"), true))
            {
                using var spinner = new ConsoleSpinner(Cli.S(cfg, "job_heal"));
                report = await Doctor.HealAsync(
                    CehoConfig.Load(Ceho.ConfigPath), Ceho.ConfigPath, Ceho.Root, tools, spinner.AsReport());
                spinner.Done(Doctor.Say(report, cfg.Language));

                Console.WriteLine();
                Cli.PrintChecks(cfg, report.Checks);
                Cli.PrintDoctorDeeds(cfg, report);
            }
        }

        Console.WriteLine();
        Console.WriteLine(Doctor.Headline(report, cfg.Language));
        return report.Healthy ? 0 : 1;
    }

    case "verify":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (cfg.Apps.Count == 0) { Cli.Stuck(cfg, Cli.S(cfg, "pf_no_apps")); return 1; }

        var allOk = true;
        foreach (var app in cfg.Apps.Where(a => a.Enabled))
        {
            var r = Ceho.VerifyApp(app, cfg.TunAddress, cfg.Language);
            Console.WriteLine(app.Name);
            if (r.Problem is not null)
            {
                Console.WriteLine("   " + r.Problem);
                allOk = false;
                continue;
            }
            Console.WriteLine("   " + Cli.S(cfg, "verify_counts", r.Processes, r.Tunneled, r.Direct));
            Console.WriteLine("   " + Cli.S(cfg, r.Isolated == true ? "verify_isolated" : "verify_leaking"));
            if (r.Isolated != true) allOk = false;
        }
        return allOk ? 0 : 2;
    }

    case "stop":
    case "off":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (Autostart.IsEnabled())
        {
            Autostart.StopService();
        }

        if (DaemonControl.RequestStop(Ceho.Root))
        {
            Console.WriteLine(Cli.S(cfg, "stop_sent"));
            await Task.Delay(TimeSpan.FromSeconds(3));

            // Службу Windows завершает резко, попрощаться демон не успевает — и в системе
            // остаётся наше мёртвое устройство. Раз оно наше, за собой убираем сами.
            if (Os.IsElevated())
            {
                TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Console.WriteLine);
                TunCleanup.RemoveLeftovers(Console.WriteLine, cfg.TunAddress, Ceho.Root);
                DaemonControl.ClearRunning(Ceho.Root);
            }
            return 0;
        }

        if (NodeProbe.TunnelIsUp(cfg.TunAddress) || DaemonControl.RunningPid(Ceho.Root) is not null)
        {
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Console.WriteLine);
            TunCleanup.RemoveLeftovers(Console.WriteLine, cfg.TunAddress, Ceho.Root);
            DaemonControl.ClearRunning(Ceho.Root);
            Console.WriteLine(Cli.S(cfg, "stop_cleaned"));
            return 0;
        }

        Console.Error.WriteLine(Cli.S(cfg, "state_off"));
        return 1;
    }

    case "restart":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);

        try { await Ceho.ApplyAsync(); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); }

        if (Autostart.IsEnabled())
        {
            if (!Os.IsElevated())
            {
                Console.Error.WriteLine(Cli.S(cfg, "rules_restart_needed", Os.IsWindows ? "" : "sudo "));
                return 1;
            }
            Autostart.Restart();
            Console.WriteLine(Cli.S(cfg, "rules_applied"));
            return 0;
        }

        var port = Auth.ReadPanelPointer(Ceho.Root) ?? cfg.WebPort;
        if (DaemonControl.IsRunning(Ceho.Root))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var res = await http.PostAsync($"http://127.0.0.1:{port}/control/restart",
                    new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("tab", "state") }));
                if (res.IsSuccessStatusCode)
                {
                    Console.WriteLine(Cli.S(cfg, "rules_applied"));
                    return 0;
                }
            }
            catch { }

            if (!Os.IsElevated())
            {
                Console.Error.WriteLine(Cli.S(cfg, "rules_restart_needed", Os.IsWindows ? "" : "sudo "));
                return 1;
            }
            DaemonControl.RequestStop(Ceho.Root);
            await Task.Delay(1000);
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, _ => {});
            TunCleanup.RemoveLeftovers(_ => {}, cfg.TunAddress, Ceho.Root);
            DaemonControl.ClearRunning(Ceho.Root);
        }

        if (Os.IsElevated())
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Ceho.OwnExecutablePath, "daemon")
                {
                    UseShellExecute = Os.IsWindows,
                    CreateNoWindow = true,
                    WorkingDirectory = Ceho.Root
                };
                System.Diagnostics.Process.Start(psi);
                await Task.Delay(1500);
                if (DaemonControl.IsRunning(Ceho.Root))
                {
                    Console.WriteLine(Cli.S(cfg, "rules_applied"));
                    return 0;
                }
            }
            catch { }
        }

        Console.WriteLine(Cli.S(cfg, "rules_rebuilt"));
        Console.WriteLine(Cli.S(cfg, "rules_restart_needed", Os.IsWindows ? "" : "sudo "));
        return 0;
    }

    case "open":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var url = $"http://127.0.0.1:{cfg.WebPort}";
        if (!DaemonControl.IsRunning(Ceho.Root))
            Console.Error.WriteLine(Cli.S(cfg, "panel_not_running", Os.IsWindows ? "" : "sudo "));
        Os.OpenInBrowser(url);
        Console.WriteLine(url);
        return 0;
    }
}

if (cmd is "daemon" or "web")
{
    var cfg = CehoConfig.Load(Ceho.ConfigPath);
    var withTunnel = cmd == "daemon";

    // Демон живёт без консоли (служба, задача планировщика, launchd), поэтому всё,
    // что он рассказывает, обязано попадать в файл журнала, а не только в stderr.
    Log.EchoToConsole = true;

    SingBoxProcess? proc = null;
    string? lastError = null;
    string? exitCountry = null, exitIp = null;
    var probed = false;

    async Task<string?> StartTunnel(IStageReport? report)
    {
        if (proc is not null) return Strings.T(cfg.Language, "already_on");
        try
        {
            var c = CehoConfig.Load(Ceho.ConfigPath);

            var nodes = await Ceho.LoadAllNodesAsync(c, preferCache: true, report);

            report?.Stage(Strings.T(c.Language, "stage_writing_rules"), 92);
            await File.WriteAllTextAsync(Ceho.RuntimeConfigPath,
                SingBoxConfigGenerator.GenerateForConfig(nodes, c));

            var reason = await BringEngineUp(c, report);
            if (reason is not null
                && reason.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("адрес туннеля занят — принудительно снимаю Wintun");
                TunCleanup.ReleaseOurs(
                    Ceho.RuntimeConfigPath, c.TunAddress, Ceho.Root, Log.Info,
                    attempts: 3, aggressive: true);
                await Task.Delay(TimeSpan.FromSeconds(2));
                reason = await BringEngineUp(c, report);
            }

            if (reason is not null)
            {
                lastError = reason;
                return reason;
            }

            lastError = null;
            probed = false;
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("защита не включилась", ex);
            lastError = ex.Message;
            return ex.Message;
        }
    }

    async Task<string?> BringEngineUp(CehoConfig c, IStageReport? report)
    {
        report?.Stage(Strings.T(c.Language, "stage_cleanup"), 94);
        var before = TunCleanup.Devices();
        // Движок от упавшего прошлого сеанса нам не сын: демон его не убьёт, уходя,
        // а порт прокси он держит — и новый запуск падает на «адрес уже занят».
        var killed = TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Log.Info);
        if (killed > 0) await Task.Delay(500);
        TunCleanup.RemoveLeftovers(Log.Info, c.TunAddress, Ceho.Root, before, Ceho.RuntimeConfigPath);

        report?.Stage(Strings.T(c.Language, "stage_engine_start"), 96);

        Os.AdoptOwnEngine(Ceho.Root);
        var p = new SingBoxProcess();
        p.Start(Ceho.SingBoxPath, Ceho.RuntimeConfigPath, Ceho.Root);
        Log.Info($"движок запущен, pid {p.ProcessId}");

        report?.Stage(Strings.T(c.Language, "stage_engine_wait"), 98);
        await Task.Delay(TimeSpan.FromSeconds(2));
        TunCleanup.Remember(Ceho.Root, c.TunAddress, Log.Info, before);
        if (p.IsRunning)
        {
            proc = p;
            return null;
        }

        // Сам вывод движка уже в журнале: он попадает туда строкой за строкой.
        var reason = p.Explain(c.Language);
        Log.Error($"движок не устоял: {reason}");
        p.Dispose();
        TunCleanup.RemoveLeftovers(Log.Info, c.TunAddress, Ceho.Root, before, Ceho.RuntimeConfigPath);
        return reason;
    }

    string? StopTunnel()
    {
        if (proc is null) return Strings.T(cfg.Language, "already_off");
        var clean = proc.Stop(8000);
        proc.WaitForExit();
        proc.Dispose();
        proc = null;
        exitCountry = exitIp = null;
        probed = false;

        if (!clean)
        {
            Log.Warn("движок не завершился по-хорошему, снимаю следы");
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Log.Info);
            Thread.Sleep(800);
        }

        TunCleanup.ReleaseOurs(
            Ceho.RuntimeConfigPath, cfg.TunAddress, Ceho.Root, Log.Info,
            attempts: 2, aggressive: false);
        return null;
    }

    var web = new WebServer(
        Ceho.ConfigPath,
        () => new WebServer.ControlState(proc is not null, exitCountry, exitIp, lastError, probed),
        Log.Info);

    async Task<string?> RestartTunnel(IStageReport? report)
    {
        report?.Stage(Strings.T(cfg.Language, "stage_stopping"), 5);
        StopTunnel();
        return await StartTunnel(report);
    }

    web.OnStart = StartTunnel;
    web.OnStop = () => Task.FromResult(StopTunnel());
    web.OnRestart = RestartTunnel;
    web.OnApply = Ceho.ApplyAsync;
    web.WrappedNames = Cli.Wrapped;
    web.OnCheckSubs = async report =>
    {
        var c = CehoConfig.Load(Ceho.ConfigPath);
        var nodes = await Ceho.LoadAllNodesAsync(c, preferCache: false, report);
        return Strings.T(c.Language, "sub_checked_nodes", nodes.Count);
    };

    web.OnUpdate = async (install, report) =>
    {
        var c = CehoConfig.Load(Ceho.ConfigPath);
        report.Stage(Strings.T(c.Language, "job_update_check"), 20);
        var release = await Updater.CheckAsync(c.UpdateRepo);
        if (release is null) return Strings.T(c.Language, "upd_none");
        if (!install) return Strings.T(c.Language, "upd_found", release.Version);

        report.Stage(Strings.T(c.Language, "upd_stopping_tun"), 25);
        StopTunnel();
        var shut = TunnelShutdown.Release(c, Ceho.Root, Ceho.RuntimeConfigPath, m => report.Note(m));
        if (!shut.Ok) return Strings.T(c.Language, shut.ErrorKey ?? "upd_need_reboot");

        try
        {
            report.Stage(Strings.T(c.Language, "stage_writing_rules"), 35);
            await Ceho.ApplyAsync(report);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        report.Stage(Strings.T(c.Language, "stage_download",
            release.Version, release.Size / 1024 / 1024), 40);
        await Updater.InstallAsync(release, Ceho.OwnExecutablePath, m => report.Note(m));

        report.Stage(Strings.T(c.Language, "stage_installing"), 90);
        Cli.MakeShortcut(Ceho.OwnExecutablePath, out _);
        report.Stage(Strings.T(c.Language, "upd_relaunch"), 95);
        DaemonControl.SpawnRelaunchHelper(Ceho.OwnExecutablePath, Ceho.Root);
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            Environment.Exit(0);
        });
        return Strings.T(c.Language, "upd_done", release.Version);
    };
    web.OnPool = report =>
        Ceho.LoadAllNodesAsync(CehoConfig.Load(Ceho.ConfigPath), preferCache: false, report);
    web.OnExit = () => Ceho.ProbeExitAsync(CehoConfig.Load(Ceho.ConfigPath).MixedPort);
    web.OnCountries = async report =>
    {
        var nodes = await Ceho.LoadAllNodesAsync(
            CehoConfig.Load(Ceho.ConfigPath), preferCache: false, report);
        report.Stage(Strings.T(cfg.Language, "speed_measuring"), 90);
        return await NodeProbe.ByCountryAsync(nodes);
    };

    web.OnUninstall = () =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            StopTunnel();
            Autostart.Purge();
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, _ => {});
            TunCleanup.KillOurProcesses(Installer.BinaryPath(Ceho.Root) + " daemon", _ => {});
            TunCleanup.RemoveLeftovers(_ => {}, cfg.TunAddress, Ceho.Root);
            DaemonControl.ClearRunning(Ceho.Root);
            try
            {
                foreach (var f in new[] { Ceho.ConfigPath, Ceho.RuntimeConfigPath })
                    if (File.Exists(f)) File.Delete(f);
                foreach (var f in Directory.GetFiles(Ceho.Root, "sub-*.txt")) File.Delete(f);
                foreach (var f in Directory.GetFiles(Ceho.Root, "*.log")) File.Delete(f);
                var pointer = Path.Combine(Ceho.Root, "panel.port");
                if (File.Exists(pointer)) File.Delete(pointer);
            }
            catch { }
            Installer.Remove(Ceho.Root, _ => {}, cfg.Language);
            foreach (var name in new[] { Os.EngineFileName, Os.SingBoxFileName }.Distinct())
            {
                var ourEngine = Path.Combine(Ceho.Root, name);
                if (File.Exists(ourEngine))
                    try { File.Delete(ourEngine); } catch { }
            }
            if (Os.IsWindows)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{Ceho.Root}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch { }
            }
            Environment.Exit(0);
        });
        return Task.FromResult(Strings.T(cfg.Language, "uninstall_done"));
    };

    web.OnApiCommand = argv => Task.Run(() =>
    {
        if (argv.Length == 0 || !Cli.CanRunRemotely(argv[0]))
            return (false, Strings.T(cfg.Language, "remote_not_allowed", argv.Length > 0 ? argv[0] : ""));

        var psi = new System.Diagnostics.ProcessStartInfo(Ceho.OwnExecutablePath)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in argv) psi.ArgumentList.Add(a);

        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return (false, "cannot start");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(60000);
        return (p.ExitCode == 0, (stdout + stderr).Trim());
    });

    var preflight = Preflight.Run(CehoConfig.Load(Ceho.ConfigPath), Ceho.Root);
    var startupBlockers = preflight.Where(c => c.Level == Preflight.Level.Blocker).ToList();
    if (startupBlockers.Count > 0)
    {
        Console.Error.WriteLine(Strings.T(cfg.Language, "startup_blockers"));
        foreach (var b in startupBlockers)
        {
            Console.Error.WriteLine($"  {b.Title}");
            if (b.Fix is not null) Console.Error.WriteLine($"    {b.Fix}");
        }
    }

    try { web.Start(cfg.WebPort); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{Strings.T(cfg.Language, "pf_port_busy", cfg.WebPort)}: {ex.Message}");
        Console.Error.WriteLine(Strings.T(cfg.Language, "pf_port_fix", Preflight.NextFreePort(cfg.WebPort)));
        return 1;
    }

    if (withTunnel)
    {
        var tunnelBlockers = startupBlockers
            .Where(c => !c.Title.Contains("орт ", StringComparison.OrdinalIgnoreCase)
                     && !c.Title.Contains("ort ", StringComparison.OrdinalIgnoreCase)).ToList();
        if (tunnelBlockers.Count > 0)
            Console.Error.WriteLine(Strings.T(cfg.Language, "panel_only", cfg.WebPort));
        else
        {
            var err = await StartTunnel(null);
            Log.Info(err is null
                ? Strings.T(cfg.Language, "state_on")
                : $"{Strings.T(cfg.Language, "start_failed")}: {err}");
        }
    }

    DaemonControl.MarkRunning(Ceho.Root);

    Auth.RestrictConfigAccess(Ceho.ConfigPath);

    using var cts = new CancellationTokenSource();
    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            if (proc is not null && !proc.IsRunning)
            {
                var reason = proc.Explain(cfg.Language);
                Log.Error($"{Strings.T(cfg.Language, "engine_gone")}: {reason}");
                lastError = reason;
                StopTunnel();
                var again = await StartTunnel(null);
                Log.Info(again is null
                    ? Strings.T(cfg.Language, "state_on")
                    : $"{Strings.T(cfg.Language, "start_failed")}: {again}");
            }

            if (proc is not null)
            {
                var port = CehoConfig.Load(Ceho.ConfigPath).MixedPort;
                (exitCountry, exitIp) = await Ceho.ProbeExitAsync(port);
                probed = true;

                if (exitIp is null)
                {
                    var refreshed = await Ceho.RefreshIfDeadAsync(port);
                    if (refreshed is not null)
                    {
                        Log.Warn(refreshed);
                        lastError = refreshed;
                        if (refreshed.Contains("обнов") || refreshed.Contains("updated"))
                        {
                            StopTunnel();
                            await StartTunnel(null);
                        }
                    }
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), cts.Token); } catch { return; }
        }
    });

    DaemonControl.WaitForStop();
    cts.Cancel();
    StopTunnel();
    web.Stop();
    DaemonControl.ClearRunning(Ceho.Root);
    Console.Error.WriteLine(Strings.T(cfg.Language, "stopped"));
    return 0;
}

Console.Error.WriteLine(Strings.T(cfg0.Language, "err_unknown_command", cmd));
return 1;

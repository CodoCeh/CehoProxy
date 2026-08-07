using ProxyCage.Core;
using ProxyCage.Cli;

// CehoProxy — изоляция выбранных программ в туннель.
// Один слой команд на CLI и веб-панель (см. Ceho.cs).

CehoConfig cfg0;
try { cfg0 = CehoConfig.Load(Ceho.ConfigPath); }
catch { cfg0 = new CehoConfig(); }   // настройки закрыты правами — режим определим ниже

// последняя страховка: человеку в терминале нужен понятный отказ, а не стек вызовов
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var ex = e.ExceptionObject as Exception;
    Console.Error.WriteLine(ex is UnauthorizedAccessException
        ? Strings.T(cfg0.Language, "no_write_access", Ceho.ConfigPath,
            Os.IsWindows ? "" : "sudo ")
        : ex?.Message ?? "непредвиденная ошибка");
    Environment.Exit(1);
};

// «chp» без аргументов — это разговор, а не справочник: показываем состояние
// и спрашиваем о том, чего не хватает. В скрипте и в службе спрашивать не у кого,
// поэтому там выводится обычная справка.
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

    string installed;
    try { installed = Installer.Install(Ceho.Root, m => Console.WriteLine("  " + m)); }
    catch (Exception ex) { Console.Error.WriteLine("  " + ex.Message); return 1; }

    if (Os.ResolveSingBox(Ceho.Root) is null
        && (args.Contains("--with-engine") || Cli.AskYes(Strings.T(cfg0.Language, "inst_engine_ask"), true)))
    {
        try { await Installer.DownloadEngineAsync(Ceho.Root, m => Console.WriteLine("  " + m)); }
        catch (Exception ex)
        {
            Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_engine_failed", ex.Message));
            Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_engine_skip"));
        }
    }

    Console.WriteLine();
    Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_done"));
    Console.WriteLine("  " + Strings.T(cfg0.Language, "product_page_at", Brand.RepoUrl(cfg0.UpdateRepo)));

    // сразу за установкой — мастер: иначе человек остаётся один на один с пустым конфигом
    if (!args.Contains("--no-setup")) return await Cli.SetupAsync(Ceho.ConfigPath);

    Console.WriteLine("  " + Strings.T(cfg0.Language, "setup_hint"));
    return 0;
}

if (cmd == "setup") return await Cli.SetupAsync(Ceho.ConfigPath);

// версия — про саму программу, доступ к настройкам для неё не нужен
if (cmd == "version")
{
    Console.WriteLine(Updater.CurrentVersion);
    Console.WriteLine(Brand.RepoUrl(cfg0.UpdateRepo));
    return 0;
}

// wrap/unwrap/wrapped живут в домашней папке пользователя и настроек продукта не трогают:
// заворачивать свои команды может каждый, и прав администратора для этого не нужно
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

    // без имени команды показываем, что вообще можно перевести: у терминальных агентов
    // имя команды и имя инструмента совпадают не всегда, и угадывать его человек не обязан
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

// «run» — команда обычного пользователя: он заворачивает СВОЙ процесс и не обязан
// иметь прав на настройки. Порт берём из публичного указателя рядом с ними.
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

// нет прав на файл настроек — значит это обычный пользователь общей машины.
// Не показываем ему пустоту, а выполняем команду через панель от имени службы.
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

// команды, меняющие настройки, требуют пароль — если он задан и вы не администратор
if (!Cli.Allowed(cfg0, args, out var authError))
{
    Console.Error.WriteLine(authError);
    return 1;
}

switch (cmd)
{
    case "add-app":
    {
        // без пути не отказываем, а показываем найденное и спрашиваем: путь к ИИ-инструменту
        // человек наизусть не помнит, а в каждой системе он свой
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
            Console.WriteLine($"{a.Name,-24} {a.Folder}");
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

    case "sub-add":
    {
        // ссылку спрашиваем и сразу проверяем: сколько нод и каких стран получилось
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
        await Assistant.DescribePoolAsync(cfg);
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
            Console.WriteLine($"{s.Name,-16} {state,-14} {s.Url}");
        }
        Console.WriteLine();
        Console.WriteLine(Cli.S(cfg, "subs_pool"));
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
        try { nodes = await Ceho.LoadAllNodesAsync(cfg); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

        Cli.PrintCountries(cfg, nodes);
        Console.WriteLine();
        Console.WriteLine("  " + Cli.S(cfg, "countries_hint"));
        Console.WriteLine("  chp country off RU · chp country on DE · chp country only NL");
        return 0;
    }

    case "country":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);

        // без аргументов — список стран, где строка переключается номером:
        // помнить коды и писать их руками ради «убрать Германию» незачем
        if (args.Length < 2)
        {
            if (!Assistant.Interactive) { Console.Error.WriteLine(Cli.S(cfg, "err_country_usage")); return 1; }
            return await Cli.CountryMenuAsync(cfg);
        }

        // запомним, что было: настройку, при которой пул пустеет, сохранять нельзя —
        // иначе человек уходит с конфигурацией, при которой туннель просто не поднимется
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

            await Updater.InstallAsync(release, Ceho.OwnExecutablePath, Console.WriteLine);
            Cli.MakeShortcut(Ceho.OwnExecutablePath, out _);

            if (DaemonControl.IsRunning(Ceho.Root))
            {
                DaemonControl.RequestStop(Ceho.Root);
                await Task.Delay(TimeSpan.FromSeconds(6));
                if (Autostart.IsEnabled()) Autostart.Restart();
            }
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
            var already = cfg.Apps.Any(a =>
                a.Folder.Equals(t.Path, StringComparison.OrdinalIgnoreCase) ||
                t.Path.StartsWith(a.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            var mark = already ? "[x]" : "[ ]";
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

        // при поднятом туннеле замер врёт: любая нода отвечает локально за пару миллисекунд
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

        // замеру нельзя верить — молча отступаем, причину уже объяснили
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
            Console.WriteLine(Cli.S(cfg, "uninstall_warn"));
            Console.WriteLine($"  {Ceho.Root}");
            Console.WriteLine(Cli.S(cfg, "uninstall_confirm"));
            return 1;
        }

        Autostart.Purge();
        if (DaemonControl.RequestStop(Ceho.Root))
            await Task.Delay(TimeSpan.FromSeconds(8));

        // мягкая остановка могла не сработать: служба бывает запущена не нами и без pid-файла.
        // Оставить после удаления живой туннель и открытую панель нельзя
        TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Console.WriteLine);
        TunCleanup.KillOurProcesses(Installer.BinaryPath(Ceho.Root) + " daemon", Console.WriteLine);

        // следы туннеля снимаем после остановки: на Linux оставленные правила
        // маршрутизации кладут сеть машины целиком, а не только изолированных программ
        TunCleanup.RemoveLeftovers(Console.WriteLine);
        DaemonControl.ClearRunning(Ceho.Root);

        foreach (var f in new[] { Ceho.ConfigPath, Ceho.RuntimeConfigPath })
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        try
        {
            foreach (var f in Directory.GetFiles(Ceho.Root, "sub-*.txt")) File.Delete(f);
            foreach (var f in Directory.GetFiles(Ceho.Root, "*.log")) File.Delete(f);
            // указатель на панель — тоже наш след, без него в папке остаётся мусор
            var pointer = Path.Combine(Ceho.Root, "panel.port");
            if (File.Exists(pointer)) File.Delete(pointer);
        }
        catch { }

        Installer.Remove(Ceho.Root, Console.WriteLine);

        // движок убираем, только если он лежит в НАШЕЙ папке: туда его кладём мы сами,
        // а системный из пакетного менеджера трогать нельзя — он не наш
        var ourEngine = Path.Combine(Ceho.Root, Os.SingBoxFileName);
        if (File.Exists(ourEngine))
            try { File.Delete(ourEngine); Console.WriteLine($"удалён движок: {ourEngine}"); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); }

        Console.WriteLine(Cli.S(cfg, "uninstall_done"));

        // сам файл программы сносим последним: после этого выполняться уже нечему
        if (args.Contains("--purge") && !Os.IsWindows)
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
        try { Console.WriteLine(await Ceho.ApplyAsync()); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

    case "status":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        // «служба работает» и «туннель поднят» — разные вещи: служба может быть жива,
        // а туннель не встать. Показывать в этом случае «защита включена» — врать в лицо
        var daemon = DaemonControl.IsRunning(Ceho.Root);
        var tunnel = NodeProbe.TunnelIsUp(cfg.TunAddress);
        var running = daemon && tunnel;
        Console.WriteLine(running ? Cli.S(cfg, "state_on")
            : daemon ? Cli.S(cfg, "state_broken")
            : Cli.S(cfg, "state_off"));
        // туннель без службы — след аварийного завершения, и молчать о нём нельзя
        if (!daemon && tunnel)
            Console.WriteLine(Cli.S(cfg, "state_leftovers", Os.IsWindows ? "" : "sudo "));
        Console.WriteLine(Cli.S(cfg, "apps_isolated", cfg.Apps.Count(a => a.Enabled)));
        Console.WriteLine(Cli.S(cfg, "subs_count", cfg.Subscriptions.Count));
        Console.WriteLine(Cli.S(cfg, "countries_title") + ": " +
            (cfg.PreferredCountries.Count > 0
                ? string.Join(", ", cfg.PreferredCountries)
                : Cli.S(cfg, "country_any") +
                  (cfg.ExcludedCountries.Count > 0 ? $" (−{string.Join(", ", cfg.ExcludedCountries)})" : "")));
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
        var checks = Preflight.Run(cfg, Ceho.Root);
        Cli.PrintChecks(cfg, checks);
        var blockers = checks.Count(c => c.Level == Preflight.Level.Blocker);
        Console.WriteLine();
        Console.WriteLine(blockers == 0 ? Cli.S(cfg, "pf_all_ready") : Cli.S(cfg, "pf_blockers", blockers));
        return blockers == 0 ? 0 : 1;
    }

    case "verify":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (cfg.Apps.Count == 0) { Console.Error.WriteLine(Cli.S(cfg, "pf_no_apps")); return 1; }

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
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (DaemonControl.RequestStop(Ceho.Root))
        {
            Console.WriteLine(Cli.S(cfg, "stop_sent"));
            return 0;
        }

        // службы нет, но туннель в системе может остаться: после аварийного завершения
        // движок живёт сам по себе, и трафик изолированных программ продолжает уходить в него.
        // «Выключить» обязано выключать и такое состояние, иначе снять его нечем
        if (NodeProbe.TunnelIsUp(cfg.TunAddress) || DaemonControl.RunningPid(Ceho.Root) is not null)
        {
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Console.WriteLine);
            TunCleanup.RemoveLeftovers(Console.WriteLine);
            DaemonControl.ClearRunning(Ceho.Root);
            Console.WriteLine(Cli.S(cfg, "stop_cleaned"));
            return 0;
        }

        Console.Error.WriteLine(Cli.S(cfg, "state_off"));
        return 1;
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

// ── демон: защита + панель ────────────────────────────────────────────
if (cmd is "daemon" or "web")
{
    var cfg = CehoConfig.Load(Ceho.ConfigPath);
    var withTunnel = cmd == "daemon";

    SingBoxProcess? proc = null;
    string? lastError = null;
    string? exitCountry = null, exitIp = null;
    var probed = false;

    async Task<string?> StartTunnel()
    {
        if (proc is not null) return Strings.T(cfg.Language, "already_on");
        try
        {
            var c = CehoConfig.Load(Ceho.ConfigPath);
            var nodes = await Ceho.LoadAllNodesAsync(c);
            await File.WriteAllTextAsync(Ceho.RuntimeConfigPath,
                SingBoxConfigGenerator.GenerateForConfig(nodes, c));

            // самолечение: снять следы аварийного завершения до того, как поднимем свой туннель
            TunCleanup.RemoveLeftovers(m => Console.Error.WriteLine(m));

            var p = new SingBoxProcess();
            p.Start(Ceho.SingBoxPath, Ceho.RuntimeConfigPath, Ceho.Root);

            // движок падает не сразу, а на конфигурации интерфейса: без этой паузы
            // мы бы отрапортовали «включено» и оставили пользователя без изоляции
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (!p.IsRunning)
            {
                var reason = p.LastLog ?? Strings.T(c.Language, "engine_died");
                p.Dispose();
                TunCleanup.RemoveLeftovers(m => Console.Error.WriteLine(m));
                lastError = reason;
                return reason;
            }

            proc = p;
            lastError = null;
            probed = false;          // новая сессия — состояние выхода ещё неизвестно
            return null;
        }
        catch (Exception ex) { lastError = ex.Message; return ex.Message; }
    }

    string? StopTunnel()
    {
        if (proc is null) return Strings.T(cfg.Language, "already_off");
        var clean = proc.Stop();
        proc.Dispose();
        proc = null;
        exitCountry = exitIp = null;
        probed = false;
        // если штатно остановиться не вышло, мусор снимаем прямо сейчас, а не ждём
        // следующего старта: на Linux оставленные правила ложат сеть всей машины
        if (!clean) TunCleanup.RemoveLeftovers(m => Console.Error.WriteLine(m));
        return null;
    }

    var web = new WebServer(
        Ceho.ConfigPath,
        () => new WebServer.ControlState(proc is not null, exitCountry, exitIp, lastError, probed),
        m => Console.Error.WriteLine(m));

    web.OnStart = StartTunnel;
    web.OnStop = () => Task.FromResult(StopTunnel());
    web.OnApply = async () => await Ceho.ApplyAsync();
    web.WrappedNames = Cli.Wrapped;
    web.OnCheckSubs = async () =>
    {
        // перечитываем подписки: состояние каждой выставит сама загрузка, по факту нод
        var c = CehoConfig.Load(Ceho.ConfigPath);
        try
        {
            var nodes = await Ceho.LoadAllNodesAsync(c);
            return Strings.T(c.Language, "sub_checked_nodes", nodes.Count);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    };

    web.OnUpdate = async install =>
    {
        var c = CehoConfig.Load(Ceho.ConfigPath);
        var release = await Updater.CheckAsync(c.UpdateRepo);
        if (release is null) return Strings.T(c.Language, "upd_none");
        if (!install) return Strings.T(c.Language, "upd_found", release.Version);

        await Updater.InstallAsync(release, Ceho.OwnExecutablePath, m => Console.Error.WriteLine(m));
        Cli.MakeShortcut(Ceho.OwnExecutablePath, out _);
        if (Autostart.IsEnabled()) Autostart.Restart();
        return Strings.T(c.Language, "upd_done", release.Version);
    };
    web.OnPool = async () => await Ceho.LoadAllNodesAsync(CehoConfig.Load(Ceho.ConfigPath));
    web.OnCountries = async () =>
        await NodeProbe.ByCountryAsync(await Ceho.LoadAllNodesAsync(CehoConfig.Load(Ceho.ConfigPath)));

    // команда из терминала пользователя без прав: выполняем СВОИМ ЖЕ бинарником, но от имени
    // службы. Это не дублирует логику команд и гарантирует ровно то же поведение, что локально.
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
        lastError = string.Join(" · ", startupBlockers.Select(b => b.Title));
    }

    try { web.Start(cfg.WebPort); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{Strings.T(cfg.Language, "pf_port_busy", cfg.WebPort)}: {ex.Message}");
        Console.Error.WriteLine(Strings.T(cfg.Language, "pf_port_fix", cfg.WebPort + 1));
        return 1;
    }

    if (withTunnel)
    {
        // порт панели тут уже занят нами, поэтому смотрим только то, что мешает именно туннелю
        var tunnelBlockers = startupBlockers
            .Where(c => !c.Title.Contains("орт ", StringComparison.OrdinalIgnoreCase)
                     && !c.Title.Contains("ort ", StringComparison.OrdinalIgnoreCase)).ToList();
        if (tunnelBlockers.Count > 0)
            Console.Error.WriteLine(Strings.T(cfg.Language, "panel_only", cfg.WebPort));
        else
        {
            var err = await StartTunnel();
            if (err is not null) Console.Error.WriteLine($"{Strings.T(cfg.Language, "start_failed")}: {err}");
        }
    }

    DaemonControl.MarkRunning(Ceho.Root);
    // папка должна оставаться читаемой: внутри указатель на панель, по которому
    // обычный пользователь узнаёт, куда обращаться. Секрет закрыт правами самого файла.
    Auth.RestrictConfigAccess(Ceho.ConfigPath);

    // фоновая проба выхода — панель показывает реальный IP, а не «должно работать»
    using var cts = new CancellationTokenSource();
    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
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
                        Console.Error.WriteLine(refreshed);
                        lastError = refreshed;
                        if (refreshed.Contains("обнов") || refreshed.Contains("updated"))
                        {
                            StopTunnel();
                            await StartTunnel();
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

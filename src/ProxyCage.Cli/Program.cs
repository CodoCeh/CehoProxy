using ProxyCage.Core;
using ProxyCage.Cli;

Os.RestartWithoutPrecompiledCodeOnMac(args);

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch { }

CehoConfig cfg0;
try { cfg0 = CehoConfig.Load(Ceho.ConfigPath); }
catch (Exception loadError)
{
    cfg0 = new CehoConfig();
    Assistant.ConfigUnreadable = loadError is UnauthorizedAccessException && File.Exists(Ceho.ConfigPath);
}

Log.Init(Ceho.Root, args.Length > 0 ? args[0] : "chp", !Cli.IsReadOnlyCommand(args));

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
    if (!Assistant.Interactive)
    {
        Assistant.PrintState(cfg0);
        Console.WriteLine();
        Cli.PrintMainCommands(cfg0);
        return 0;
    }
    return await Assistant.RunAsync();
}

var cmd = args[0];

if (cmd is "help" or "--help" or "-h" or "/?")
{
    Cli.PrintHelp(cfg0);
    return 0;
}

if (await Cli.EnsureRightsAsync(args, cfg0) is { } handledElevated) return handledElevated;

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

    await TrayInstaller.EnsureAsync(Ceho.Root, cfg0.UpdateRepo, true, m => Console.WriteLine("  " + m), cfg0.Language);
    if (Os.IsWindows && Os.IsElevated()) TrayInstaller.StartInWindowsSessions(Ceho.Root);

    Console.WriteLine();
    Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_done"));
    Console.WriteLine("  " + Strings.T(cfg0.Language, "product_page_at", Brand.RepoUrl(cfg0.UpdateRepo)));

    // Обновление не должно оставлять машину без защиты: что работало — включаем обратно.
    // irm|iex / install.ps1 гасит процесс ДО «install --no-setup», поэтому WasRunning
    // часто false — тогда поднимаем демон, если есть сохранённые настройки.
    var resume = previous.WasRunning
        || previous.AutostartWasOn
        || (args.Contains("--no-setup") && File.Exists(Ceho.ConfigPath));
    if (previous.AutostartWasOn)
    {
        var err = Autostart.Enable(Installer.BinaryPath(Ceho.Root), Ceho.Root);
        Console.WriteLine("  " + (err ?? Strings.T(cfg0.Language, "inst_autostart_back")));
        if (err is null) Autostart.Restart();
        if (!DaemonControl.WaitUntilRunning(Ceho.Root) && !DaemonControl.TryStart(installed, Ceho.Root))
            Console.WriteLine("  " + Strings.T(cfg0.Language, "inst_resume_failed",
                Os.IsWindows ? "" : "sudo ", installed));
    }
    else if (resume)
    {
        Console.WriteLine("  " + (DaemonControl.TryStart(installed, Ceho.Root)
            ? Strings.T(cfg0.Language, "inst_resumed")
            : Strings.T(cfg0.Language, "inst_resume_failed",
                Os.IsWindows ? "" : "sudo ", installed)));
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
    if (args.Length >= 2 && args[1].ToLowerInvariant() is "auto" or "авто")
    {
        var engineCfg = CehoConfig.Load(Ceho.ConfigPath);
        if (args.Length >= 3 && args[2] is "on" or "off")
        {
            engineCfg.EngineAutoUpdate = args[2] == "on";
            engineCfg.Save(Ceho.ConfigPath);
            Console.WriteLine(Strings.T(engineCfg.Language,
                engineCfg.EngineAutoUpdate ? "engine_auto_state_on" : "engine_auto_state_off"));
            return 0;
        }
        if (args.Length >= 3)
        {
            Console.Error.WriteLine("engine auto on | engine auto off");
            return 1;
        }
        Console.WriteLine(Strings.T(engineCfg.Language, engineCfg.EngineAutoUpdate ? "engine_auto_on" : "engine_auto_off"));
        return 0;
    }

    var already = Os.ResolveSingBox(Ceho.Root);
    var again = args.Length >= 2 && args[1].ToLowerInvariant() is "update" or "обновить" or "--force";

    if (already is not null && !again && !Installer.MissingCronetDll(Ceho.Root))
    {
        var have = Installer.EngineVersionOf(already);
        Console.WriteLine(Strings.T(cfg0.Language, "engine_already", already));
        Console.WriteLine(have is null ? Strings.T(cfg0.Language, "engine_line_unknown")
            : Installer.EngineOutdated(have) ? Strings.T(cfg0.Language, "engine_line_old", have, Installer.EngineVersion)
            : Strings.T(cfg0.Language, "engine_line", have));
        if (Installer.EngineOutdated(have))
            Console.WriteLine(Strings.T(cfg0.Language, "engine_update_hint", Os.IsWindows ? "" : "sudo "));
        return 0;
    }

    if (already is not null && again && !Installer.MissingCronetDll(Ceho.Root))
    {
        if (DaemonControl.IsRunning(Ceho.Root) && Auth.ReadPanelPointer(Ceho.Root) is not null)
            return await Cli.RunRemoteAsync(Ceho.Root, args, cfg0.Language);

        var have = Installer.EngineVersionOf(already);
        if (have is not null && !Installer.EngineOutdated(have))
        {
            Console.WriteLine(Strings.T(cfg0.Language, "engine_current", have));
            return 0;
        }
        try
        {
            var fresh = await Installer.PrepareEngineUpdateAsync(
                Ceho.Root, Ceho.RuntimeConfigPath, m => Console.WriteLine("  " + m), cfg0.Language);
            Installer.SwapEngine(Ceho.Root, fresh);
            Console.WriteLine(Strings.T(cfg0.Language, "engine_updated", Installer.EngineVersion));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Strings.T(cfg0.Language, "engine_update_failed", ex.Message));
            return 1;
        }
        finally
        {
            Installer.DropEngineStaging(Ceho.Root);
        }
    }

    if (Installer.MissingCronetDll(Ceho.Root))
        Console.WriteLine(Strings.T(cfg0.Language, "engine_cronet_missing", Ceho.Root));

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

if (cmd == "setup")
    return args.Any(a => a is "--sub" or "--all-apps" or "--app" or "--autostart" or "--lang")
        ? await Cli.SetupFromFlagsAsync(Ceho.ConfigPath, args)
        : await Cli.SetupAsync(Ceho.ConfigPath);

if (cmd == "version")
{
    Console.WriteLine(Updater.CurrentVersion);
    Console.WriteLine(Brand.RepoUrl(cfg0.UpdateRepo));
    return 0;
}

if (cmd == "update-status")
{
    var status = UpdateHandoff.Read(Ceho.Root);
    if (status is null)
    {
        Console.Error.WriteLine("Нет сохранённого результата обновления.");
        return 1;
    }

    Console.WriteLine(status.Message);
    return UpdateHandoff.ExitCode(status);
}

if (cmd == "_prepare-install")
{
    if (!OperatingSystem.IsWindows() || !Os.IsElevated()) return 1;

    Autostart.StopService();
    DaemonControl.RequestStop(Ceho.Root);
    DaemonControl.WaitForExit(Ceho.Root, 8000);
    if (!DaemonControl.StopInstalledWindowsDaemons(Ceho.Root)) return 1;

    TunCleanup.ReleaseOurs(
        Ceho.RuntimeConfigPath, cfg0.TunAddress, Ceho.Root,
        _ => { }, attempts: 3, aggressive: true, beforeStart: TunCleanup.Devices());
    DaemonControl.ClearRunning(Ceho.Root);
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

if (cmd == "open")
{
    // Конфиг закрыт от обычного пользователя, но открыть панель в браузере
    // можно и без него: порт лежит в panel.port, секреты для этого не нужны.
    var port = CehoConfig.ReadWebPort(Ceho.ConfigPath, Ceho.Root);
    var url = $"http://127.0.0.1:{port}";
    if (!DaemonControl.IsRunning(Ceho.Root))
        Console.Error.WriteLine(Cli.S(cfg0, "panel_not_running", Os.IsWindows ? "" : "sudo "));
    Os.OpenInBrowser(url);
    Console.WriteLine(url);
    return 0;
}

if (cmd == "daemon" && DaemonControl.WantsBackground(Console.IsInputRedirected,
        Environment.GetEnvironmentVariable(DaemonControl.ForegroundEnv)))
{
    var panelPort = CehoConfig.ReadWebPort(Ceho.ConfigPath, Ceho.Root);
    var panelUrl = $"http://127.0.0.1:{panelPort}";
    if (DaemonControl.IsRunning(Ceho.Root))
    {
        Console.WriteLine(Cli.S(cfg0, "already_on"));
        Console.WriteLine(Cli.S(cfg0, "panel_at", panelUrl));
        return 0;
    }

    if (!DaemonControl.StartInBackground(Ceho.OwnExecutablePath, Ceho.Root))
    {
        Console.Error.WriteLine(Cli.S(cfg0, "daemon_background_failed"));
        return 1;
    }

    Console.WriteLine(Cli.S(cfg0, "daemon_background", panelUrl));
    return 0;
}

if (cmd != "detect-apps" && (Cli.ConfigUnreadable(Ceho.ConfigPath) ||
    (Cli.ChangesSettings(cmd) && Cli.ConfigReadOnly(Ceho.ConfigPath))))
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
        if (args.Length == 2 && (args[1] == "--all" || Assistant.IsAll(args[1])))
        {
            var all = CehoConfig.Load(Ceho.ConfigPath);
            if (!Assistant.AddAllFound(all)) { Console.WriteLine(Cli.S(all, "ask_apps_none_found")); return 0; }
            all.Save(Ceho.ConfigPath);
            Auth.RestrictConfigAccess(Ceho.ConfigPath);
            try { Console.WriteLine(await Ceho.ApplyAsync()); } catch (Exception ex) { Console.WriteLine(ex.Message); }
            return 0;
        }
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
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (!Assistant.TryAddApp(cfg, args[1], out var d, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }
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
            var how = a.NoInternet ? Cli.S(cfg, "app_offline_tag")
                : a.AllowedNodes.Count == 0
                ? Cli.S(cfg, "app_tunnel_general")
                : Cli.S(cfg, "app_tunnel_pinned", a.AllowedNodes.Count);
            Console.WriteLine($"{a.Label,-24} {a.Folder}  · {how}");
        }
        return 0;
    }

    case "no-internet":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var turn = args.Length >= 3 ? args[2].ToLowerInvariant() : "on";
        if (args.Length < 2 || turn is not ("on" or "off"))
        {
            Console.Error.WriteLine(Cli.S(cfg, "err_offline_usage"));
            return 1;
        }

        var which = args[1];
        var target = Os.RealPath(which);
        var app = int.TryParse(which, out var idx) && idx >= 1 && idx <= cfg.Apps.Count
            ? cfg.Apps[idx - 1]
            : cfg.Apps.FirstOrDefault(a =>
                a.Folder.Equals(which, StringComparison.OrdinalIgnoreCase) ||
                a.Folder.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (app is null) { Console.Error.WriteLine(Cli.S(cfg, "err_not_in_list")); return 1; }

        app.NoInternet = turn == "on";
        cfg.Save(Ceho.ConfigPath);
        Auth.RestrictConfigAccess(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, app.NoInternet ? "app_offline_saved" : "app_offline_cleared", app.Label));
        await Cli.RebuildQuietlyAsync(cfg);
        return 0;
    }

    case "remove-app":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var which = args.Length >= 2 ? args[1] : null;
        if (which is null)
        {
            if (!Assistant.Interactive) { Console.Error.WriteLine(Cli.S(cfg, "err_need_path")); return 1; }
            var i = Assistant.Pick(cfg, cfg.Apps.Select(a => $"{a.Label,-20} {a.Folder}").ToList(),
                Cli.S(cfg, "apps_title"));
            if (i < 0) return 0;
            which = cfg.Apps[i].Folder;
        }
        else if (int.TryParse(which, out var idx) && idx >= 1 && idx <= cfg.Apps.Count)
            which = cfg.Apps[idx - 1].Folder;
        else if (!cfg.Apps.Any(a => a.Folder.Equals(which, StringComparison.OrdinalIgnoreCase)
                                 || a.Folder.Equals(Os.RealPath(which), StringComparison.OrdinalIgnoreCase)))
        {
            var named = AppLookup.ByName(cfg.Apps, which);
            if (named.Count > 1)
            {
                Console.Error.WriteLine(Cli.S(cfg, "err_app_name_ambiguous", which));
                foreach (var a in named) Console.Error.WriteLine($"  {a.Folder}");
                return 1;
            }
            if (named.Count == 1) which = named[0].Folder;
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

    case "rename-app":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        AppEntry? app = null;
        string? label = null;

        if (args.Length >= 3)
        {
            var which = args[1];
            label = args[2].Trim();
            if (int.TryParse(which, out var idx) && idx >= 1 && idx <= cfg.Apps.Count)
                app = cfg.Apps[idx - 1];
            else
            {
                var target = Os.RealPath(which);
                app = cfg.Apps.FirstOrDefault(a =>
                    a.Folder.Equals(which, StringComparison.OrdinalIgnoreCase) ||
                    a.Folder.Equals(target, StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (args.Length == 2 && Assistant.Interactive)
        {
            var which = args[1];
            var target = Os.RealPath(which);
            app = cfg.Apps.FirstOrDefault(a =>
                a.Folder.Equals(which, StringComparison.OrdinalIgnoreCase) ||
                a.Folder.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (app is null && int.TryParse(which, out var idx) && idx >= 1 && idx <= cfg.Apps.Count)
                app = cfg.Apps[idx - 1];

            if (app is not null)
                label = Cli.Ask("  " + Cli.S(cfg, "rename_app_ask")).Trim();
            else
            {
                label = which.Trim();
                var i = Assistant.Pick(cfg, cfg.Apps.Select(a => $"{a.Label,-20} {a.Folder}").ToList(),
                    Cli.S(cfg, "apps_title"));
                if (i < 0) return 0;
                app = cfg.Apps[i];
            }
        }
        else if (Assistant.Interactive)
        {
            var i = Assistant.Pick(cfg, cfg.Apps.Select(a => $"{a.Label,-20} {a.Folder}").ToList(),
                Cli.S(cfg, "apps_title"));
            if (i < 0) return 0;
            app = cfg.Apps[i];
            label = Cli.Ask("  " + Cli.S(cfg, "rename_app_ask")).Trim();
        }
        else
        {
            Console.Error.WriteLine(Cli.S(cfg, "err_rename_usage"));
            return 1;
        }

        if (label is null || label.Length == 0) { Console.Error.WriteLine(Cli.S(cfg, "err_need_name")); return 1; }
        if (app is null) { Console.Error.WriteLine(Cli.S(cfg, "err_not_in_list")); return 1; }

        app.DisplayName = label;
        cfg.SaveSubscriptionStatus(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, "app_renamed", app.Label));
        return 0;
    }

    case "export":
    case "import":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var words = args.Skip(1).ToList();
        string? partText = null;
        for (var i = 0; i < words.Count; i++)
        {
            if (words[i] is "--part" or "--часть" && i + 1 < words.Count) { partText = words[i + 1]; words.RemoveRange(i, 2); break; }
            if (words[i].StartsWith("--part=") || words[i].StartsWith("--часть="))
            { partText = words[i][(words[i].IndexOf('=') + 1)..]; words.RemoveAt(i); break; }
        }
        if (words.Count < 1) { Console.Error.WriteLine(Cli.S(cfg, "transfer_usage")); return 1; }
        var file = Path.GetFullPath(words[0]);
        if (partText is null && Assistant.Interactive)
            partText = Cli.Ask("  " + Cli.S(cfg, "transfer_what_ask"));
        var parts = SettingsTransfer.ParseParts(partText);
        if (parts == SettingsTransfer.Parts.None) { Console.Error.WriteLine(Cli.S(cfg, "transfer_no_parts")); return 1; }
        var password = Environment.GetEnvironmentVariable("CEHOPROXY_TRANSFER_PASSWORD")
                       ?? Cli.AskSecret(Cli.S(cfg, "transfer_password"));
        if (args[0] == "export")
        {
            if (password.Length < 6) { Console.Error.WriteLine(Cli.S(cfg, "transfer_password_short")); return 1; }
            if (Environment.GetEnvironmentVariable("CEHOPROXY_TRANSFER_PASSWORD") is null
                && Cli.AskSecret(Cli.S(cfg, "setup_password_again")) != password)
            {
                Console.Error.WriteLine(Cli.S(cfg, "setup_password_mismatch"));
                return 1;
            }
            File.WriteAllText(file, SettingsTransfer.Export(Ceho.Root, password, parts));
            Console.WriteLine(Cli.S(cfg, "transfer_exported") + " " + file);
            return 0;
        }

        if (!File.Exists(file)) { Console.Error.WriteLine(Cli.S(cfg, "transfer_no_file")); return 1; }
        try
        {
            var result = SettingsTransfer.Import(Ceho.Root, File.ReadAllText(file), password, parts);
            var fresh = CehoConfig.Load(Ceho.ConfigPath);
            Console.WriteLine(SettingsTransfer.Describe(result, fresh.Language));
            Console.WriteLine(Cli.S(fresh, "transfer_restart_hint"));
            return 0;
        }
        catch (SettingsTransfer.WrongPasswordException) { Console.Error.WriteLine(Cli.S(cfg, "transfer_wrong_password")); return 1; }
        catch (InvalidDataException) { Console.Error.WriteLine(Cli.S(cfg, "transfer_bad_file")); return 1; }
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
            var how = a.NoInternet ? Cli.S(cfg, "app_offline_tag")
                : a.AllowedNodes.Count == 0
                ? Cli.S(cfg, "app_tunnel_general")
                : Cli.S(cfg, "app_tunnel_pinned", a.AllowedNodes.Count);
            Console.WriteLine($"  {i + 1,3}. {a.Label,-20} {how}");
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
            names.Add(cfg.Apps[i].Label);
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
        var url = args[2];
        if (NaiveProxyHelper.TryParseUri(url, out var naive) && naive is not null)
            url = NaiveProxyHelper.BuildUri(naive);
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = args[1], Url = url });
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
            Console.WriteLine($"[{(s.Enabled ? "x" : " ")}] {s.Name,-16} {state,-14} {until,-18} {WebServer.MaskUrl(s.Url)}");
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
                case "important" or "важное": view = LogView.Important; break;
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
            LogView.Important => "log_view_important",
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

        var binaryDir = Path.GetDirectoryName(Ceho.OwnExecutablePath) ?? Ceho.Root;
        if (!Preflight.FolderIsWritable(binaryDir, out var noWrite))
        {
            Console.Error.WriteLine(Cli.S(cfg, "upd_no_write", Ceho.OwnExecutablePath, noWrite));
            return 1;
        }

        try
        {
            var release = await Updater.CheckAsync(Cli.Opt(args, "--repo") ?? cfg.UpdateRepo);
            if (release is null) { Console.WriteLine(Cli.S(cfg, "upd_none")); return 0; }

            Console.WriteLine(Cli.S(cfg, "upd_found", release.Version));
            if (!args.Contains("--yes") && !Cli.AskYes(Cli.S(cfg, "upd_apply"), true))
            { Console.WriteLine(Cli.S(cfg, "cancelled")); return 0; }

            var swept = Installer.SweepOldBinaries(binaryDir, Ceho.OwnExecutablePath);
            if (swept > 0) Console.WriteLine("  " + Cli.S(cfg, "upd_swept", swept));

            UpdateRollback.RememberProtection(Ceho.Root, NodeProbe.TunnelIsUp(cfg.TunAddress));
            var (downloaded, shut) = await Updater.DownloadThenPrepareForUpdateAsync(
                async () =>
                {
                    var staged = await Updater.DownloadAsync(release, Ceho.OwnExecutablePath, Console.WriteLine);
                    if (Installer.MissingCronetDll(Ceho.Root))
                    {
                        try
                        {
                            Console.WriteLine(Strings.T(cfg.Language, "engine_cronet_missing", Ceho.Root));
                            await Installer.DownloadEngineAsync(Ceho.Root, Console.WriteLine, cfg.Language);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(Strings.T(cfg.Language, "inst_engine_failed", ex.Message));
                        }
                    }
                    return staged;
                },
                () =>
                {
                    var keeping = DaemonControl.CanKeepEngine && DaemonControl.IsRunning(Ceho.Root)
                        && TunCleanup.IsOurEngineRunning(Ceho.RuntimeConfigPath, Ceho.Root);
                    Console.WriteLine("  " + Cli.S(cfg, keeping ? "upd_keeping_tun" : "upd_stopping_tun"));
                    return Task.FromResult(TunnelShutdown.PrepareForUpdate(
                        cfg, Ceho.Root, Ceho.RuntimeConfigPath, Console.WriteLine));
                });
            if (!shut.Ok)
            {
                Console.Error.WriteLine(Cli.S(cfg, shut.ErrorKey ?? "upd_need_reboot"));
                return 1;
            }

            try
            {
                if (cfg.Subscriptions.Count > 0 && cfg.Apps.Count > 0)
                    Console.WriteLine("  " + await Ceho.ApplyAsync());
            }
            catch (Exception ex)
            {
                if (Os.IsWindows)
                {
                    try { if (File.Exists(downloaded)) File.Delete(downloaded); } catch { }
                    TunnelShutdown.RestoreAfterUpdateHandoffFailure(shut, Ceho.Root, Console.WriteLine);
                    throw new InvalidOperationException("Не удалось применить настройки перед обновлением.", ex);
                }
                Console.WriteLine("  " + ex.Message);
            }

            if (OperatingSystem.IsWindows())
            {
                var handoffId = "cli-" + Guid.NewGuid().ToString("N");
                try
                {
                    UpdateHandoff.Write(Ceho.Root, UpdateHandoff.Pending(handoffId, release.Version));
                    DaemonControl.SpawnUpdateRelaunchHelper(
                        Ceho.OwnExecutablePath, downloaded, Ceho.Root, release.Version, handoffId,
                        inheritConsole: true);
                }
                catch
                {
                    try { if (File.Exists(downloaded)) File.Delete(downloaded); } catch { }
                    try { UpdateHandoff.Write(Ceho.Root, UpdateHandoff.Failed(handoffId, release.Version)); } catch { }
                    TunnelShutdown.RestoreAfterUpdateHandoffFailure(shut, Ceho.Root, Console.WriteLine);
                    throw;
                }

                Console.WriteLine("Замена ожидает завершения этой команды; проверьте итог через chp update-status.");
                return UpdateHandoff.PendingExitCode;
            }

            var applied = Updater.ApplyDownloaded(
                downloaded, Ceho.OwnExecutablePath, release.Version, m => Console.WriteLine("  " + m));
            bool Restore() => Updater.RestoreProtectionAfterApply(shut,
                () => DaemonControl.RestartAfterUpdate(Ceho.OwnExecutablePath, Ceho.Root, shut.AutostartWasOn));

            if (!applied.Ok)
            {
                if (!Restore()) Console.WriteLine(Cli.S(cfg, "upd_restart_failed", "sudo "));
                Console.Error.WriteLine(Cli.S(cfg, "upd_not_applied",
                    applied.Installed ?? Updater.CurrentVersion, release.Version));
                Console.Error.WriteLine(Cli.S(cfg, "upd_finish_by_hand", File.Exists(downloaded)
                    ? $"sudo install -m 755 \"{downloaded}\" \"{Ceho.OwnExecutablePath}\""
                    : "sudo chp update --yes"));
                return 1;
            }

            Console.WriteLine("  " + Cli.S(cfg, "upd_applied", release.Version));
            if (!Restore()) Console.WriteLine(Cli.S(cfg, "upd_restart_failed", "sudo "));

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
        try { Installer.LinkUserAlias(made ?? Ceho.OwnExecutablePath); }
        catch (Exception ex) { err ??= ex.Message; }
        Console.WriteLine(err is null
            ? Cli.S(cfg, "alias_made", made ?? "chp")
            : Cli.S(cfg, "alias_failed", err));
        return err is null ? 0 : 1;
    }

    case "detect-apps":
    {
        var outFile = args.Length >= 3 && args[1] == "--out" ? args[2] : null;
        var rows = new List<(char Kind, string Name, string Path)>();
        string Trim(string path) => path.TrimEnd('/', '\\');
        bool Nested(string a, string b) =>
            Trim(a).Equals(Trim(b), StringComparison.OrdinalIgnoreCase)
            || Trim(a).StartsWith(Trim(b) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Trim(b).StartsWith(Trim(a) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        void Line(char kind, string name, string path)
        {
            name = name.Replace('\t', ' ').Trim();
            if (rows.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && Nested(r.Path, path))) return;
            rows.Add((kind, name, path));
        }
        var installed = InstalledAppCatalog.Detect(Strings.Normalize("ru"));
        foreach (var t in AiTools.Detect()) Line('R', t.Name, t.Path);
        foreach (var e in InstalledAppCatalog.Recommended()) Line('R', e.Name, e.Path);
        foreach (var e in installed)
            Line(InstalledAppCatalog.GroupOf(e) switch
            {
                InstalledAppCatalog.Group.Browsers => 'B',
                InstalledAppCatalog.Group.Messengers => 'M',
                _ => 'O',
            }, e.Name, e.Path);
        var lines = rows.Select(r =>
        {
            var same = rows.Count(x => x.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase));
            var label = r.Name;
            if (same > 1)
            {
                var leaf = Path.GetFileName(Trim(r.Path));
                if (leaf.Length == 0 || leaf.Equals(r.Name, StringComparison.OrdinalIgnoreCase))
                    leaf = Path.GetFileName(Trim(Path.GetDirectoryName(Trim(r.Path)) ?? ""));
                label = leaf.Length > 0 ? $"{r.Name} ({leaf})" : r.Name;
            }
            return $"{r.Kind}\t{label}\t{r.Path}";
        }).ToList();
        if (outFile is not null) File.WriteAllLines(outFile, lines, new System.Text.UTF8Encoding(true));
        else foreach (var l in lines) Console.WriteLine(l);
        return 0;
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
                Console.WriteLine("      " + Cli.S(cfg, "ai_script_warn",
                    Path.GetFileName(t.Interpreter), AiTools.SuggestedCommand(t)));
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

    case "proxy-test":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var r = ProxyProbe.TestMixed(cfg.MixedPort, null);
        Console.WriteLine(ProxyProbe.FormatResult(r, cfg.Language, null));
        return r.Ok ? 0 : 1;
    }

    case "ping" or "test-conn":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var target = string.IsNullOrWhiteSpace(cfg.CheckUrl) ? ConnPing.DefaultTarget : cfg.CheckUrl;
        Console.WriteLine(Cli.S(cfg, "cli_ping_title", target));
        Console.WriteLine();

        ConnPingReport r;
        using (var spinner = new ConsoleSpinner(Cli.S(cfg, "job_ping_test")))
        {
            r = await ConnPing.RunAsync(cfg, (msg, pct) => spinner.Update(msg, pct));
        }

        Console.WriteLine("1/2 " + Cli.S(cfg, "ping_direct_title"));
        if (r.Direct.Ok)
        {
            Console.WriteLine($"    {Cli.S(cfg, "ping_result_stat", r.Direct.SuccessCount, r.Direct.TotalAttempts, r.Direct.SuccessPercent, r.Direct.AvgMs)}");
        }
        else
        {
            Console.WriteLine($"    {Cli.S(cfg, "ping_result_stat_fail", r.Direct.TotalAttempts, r.Direct.LastError ?? "—")}");
        }
        Console.WriteLine();

        Console.WriteLine("2/2 " + Cli.S(cfg, "ping_proxy_title"));
        if (r.Proxy.Ok)
        {
            Console.WriteLine($"    {Cli.S(cfg, "ping_result_stat", r.Proxy.SuccessCount, r.Proxy.TotalAttempts, r.Proxy.SuccessPercent, r.Proxy.AvgMs)}");
        }
        else
        {
            Console.WriteLine($"    {Cli.S(cfg, "ping_result_stat_fail", r.Proxy.TotalAttempts, r.Proxy.LastError ?? "—")}");
        }
        Console.WriteLine();

        var verdict = (r.Direct.Ok, r.Proxy.Ok) switch
        {
            (true, true) => Cli.S(cfg, "ping_verdict_both"),
            (true, false) => Cli.S(cfg, "ping_verdict_proxy_down"),
            _ => Cli.S(cfg, "ping_verdict_direct_down"),
        };
        Console.WriteLine(verdict);
        return r.Proxy.Ok ? 0 : (r.Direct.Ok ? 1 : 2);
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
        if (NodeProbe.MeasureBlocked(cfg.TunAddress, TunCleanup.IsOurEngineRunning(Ceho.RuntimeConfigPath, Ceho.Root)))
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

        if (NodeProbe.MeasureBlocked(cfg.TunAddress, TunCleanup.IsOurEngineRunning(Ceho.RuntimeConfigPath, Ceho.Root)))
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
            if (err is null && !DaemonControl.IsRunning(Ceho.Root) && File.Exists(Ceho.ConfigPath))
                Autostart.Restart();
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

    case "autoupdate":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        if (args.Length < 2)
        {
            Console.WriteLine(Cli.S(cfg, cfg.AutoUpdate ? "upd_auto_state_on" : "upd_auto_state_off"));
            return 0;
        }
        if (args[1] is not ("on" or "off"))
        {
            Console.Error.WriteLine("autoupdate on | autoupdate off");
            return 1;
        }
        cfg.AutoUpdate = args[1] == "on";
        cfg.Save(Ceho.ConfigPath);
        Console.WriteLine(Cli.S(cfg, cfg.AutoUpdate ? "upd_auto_state_on" : "upd_auto_state_off"));
        return 0;
    }

    case "uninstall":
    case "uninstal":
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

        // schtasks /end может оставить отдельный daemon живым без задания и pid-файла.
        // Не удаляем настройки, пока установленный файл всё ещё занят нашим процессом.
        if (OperatingSystem.IsWindows() && !DaemonControl.StopInstalledWindowsDaemons(Ceho.Root))
        {
            Console.Error.WriteLine("Не удалось остановить CehoProxy; удаление отменено.");
            return 1;
        }

        TunCleanup.KillOurProcesses(TunCleanup.GuardConfigPath(Ceho.Root), Console.WriteLine);
        TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Console.WriteLine);
        TunCleanup.KillOurProcesses(Installer.BinaryPath(Ceho.Root) + " daemon", Console.WriteLine);

        TunCleanup.RemoveLeftovers(Console.WriteLine, cfg.TunAddress, Ceho.Root);
        DaemonControl.ClearRunning(Ceho.Root);

        foreach (var f in new[] { Ceho.ConfigPath, Ceho.RuntimeConfigPath, TunCleanup.GuardConfigPath(Ceho.Root) })
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        try
        {
            foreach (var f in Directory.GetFiles(Ceho.Root, "sub-*.txt")) File.Delete(f);
            var pointer = Path.Combine(Ceho.Root, "panel.port");
            if (File.Exists(pointer)) File.Delete(pointer);
        }
        catch { }

        await Ceho.DisposeCountryDatabaseAsync();
        Installer.RemoveRuntimeFiles(Ceho.Root);
        Installer.Remove(Ceho.Root, Console.WriteLine, cfg.Language);

        foreach (var name in new[] { Os.EngineFileName, Os.SingBoxFileName, Installer.CronetFileName }.Distinct())
        {
            var file = Path.Combine(Ceho.Root, name);
            if (File.Exists(file))
                try { File.Delete(file); Console.WriteLine($"удалён: {file}"); }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
        }

        Console.WriteLine(Cli.S(cfg, "uninstall_done"));

        if (Os.IsWindows)
        {
            try
            {
                var binary = Installer.BinaryPath(Ceho.Root);
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
                    $"/c ping 127.0.0.1 -n 3 >nul & del /f /q \"{binary}\" & del /f /q \"{Path.Combine(Ceho.Root, "chp.cmd")}\" & rmdir /q \"{Ceho.Root}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = Environment.SystemDirectory,
                };
                try { Environment.CurrentDirectory = Environment.SystemDirectory; } catch { }
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }
        else
        {
            try
            {
                var self = Installer.BinaryPath(Ceho.Root);
                if (File.Exists(self)) File.Delete(self);
                if (args.Contains("--purge"))
                    foreach (var program in new[] { "/usr/local/bin/cehoproxy", Ceho.OwnExecutablePath }.Distinct())
                        try { if (File.Exists(program)) File.Delete(program); } catch { }
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
        if (args.Contains("--json"))
        {
            var (country, ip) = running && !args.Contains("--quick")
                ? await Ceho.ProbeExitAsync(cfg.MixedPort)
                : (null, null);
            Console.WriteLine(new System.Text.Json.Nodes.JsonObject
            {
                ["running"] = running,
                ["starting"] = daemon && !running && DaemonControl.IsStarting(Ceho.Root),
                ["recovering"] = daemon && !running && DaemonControl.IsRecovering(Ceho.Root),
                ["daemon"] = daemon,
                ["leakGuard"] = LeakGuard.IsActive(Ceho.Root),
                ["apps"] = cfg.Apps.Count(a => a.Enabled),
                ["subscriptions"] = cfg.Subscriptions.Count,
                ["exitCountry"] = country,
                ["exitIp"] = ip,
                ["password"] = Auth.HasPassword(cfg),
                ["trayControls"] = cfg.TrayControls,
                ["version"] = Updater.CurrentVersion,
            }.ToJsonString());
            return running ? 0 : 1;
        }
        string? exitCountry = null, exitIp = null;
        if (running) (exitCountry, exitIp) = await Ceho.ProbeExitAsync(cfg.MixedPort);
        Console.WriteLine(running && exitIp is null ? Cli.Paint(Cli.S(cfg, "hero_no_exit"), Preflight.Level.Blocker)
            : running ? Cli.Paint(Cli.S(cfg, "state_on"), Preflight.Level.Ok)
            : daemon && DaemonControl.IsRecovering(Ceho.Root) ? Cli.Paint(Cli.S(cfg, "state_recovering"), Preflight.Level.Warning)
            : daemon && DaemonControl.IsStarting(Ceho.Root) ? Cli.Paint(Cli.S(cfg, "state_starting"), Preflight.Level.Warning)
            : LeakGuard.IsActive(Ceho.Root) ? Cli.Paint(Cli.S(cfg, "state_off"), Preflight.Level.Warning)
            : daemon && !cfg.Apps.Any(a => a.Enabled) ? Cli.Paint(Cli.S(cfg, "state_no_apps"), Preflight.Level.Warning)
            : daemon ? Cli.Paint(Cli.S(cfg, "state_broken"), Preflight.Level.Blocker)
            : Cli.Paint(Cli.S(cfg, "state_off"), Preflight.Level.Warning));

        if (!daemon && tunnel)
            Console.WriteLine(Cli.S(cfg, "state_leftovers", Os.IsWindows ? "" : "sudo "));
        if (!running && LeakGuard.IsActive(Ceho.Root))
            Console.WriteLine(Cli.S(cfg, "state_guarded"));
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
            Console.WriteLine(exitIp is null
                ? Cli.S(cfg, "state_no_exit")
                : Strings.T(cfg.Language, "exit_is", exitCountry ?? "?", exitIp));
            if (exitIp is null)
                Console.WriteLine(Cli.S(cfg, "no_exit_todo_title") + ": " + Cli.S(cfg, "no_exit_todo"));
        }
        return 0;
    }

    case "doctor":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);
        var tools = Cli.DoctorTools();

        var wantFix = args.Length >= 2 && args[1] is "fix" or "--fix" or "heal" or "--heal";
        var quiet = args.Contains("--yes") || !Assistant.Interactive;

        if (!wantFix && !DaemonControl.IsRunning(Ceho.Root))
        {
            var moved = Preflight.SaveProxyPortIfBusy(cfg, Ceho.ConfigPath);
            if (moved is not null)
            {
                Console.WriteLine(moved);
                cfg = CehoConfig.Load(Ceho.ConfigPath);
            }
        }

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

        // Занятый порт прокси осмотр уже перенёс сам. Остальное чинится только по «doctor fix».
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
            Console.WriteLine(app.Label);
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
                TunCleanup.KillTunnelGuard(Ceho.Root, Console.WriteLine);
                TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Console.WriteLine);
                TunCleanup.ReleaseOurs(
                    Ceho.RuntimeConfigPath, cfg.TunAddress, Ceho.Root, Console.WriteLine,
                    attempts: 5, aggressive: true, beforeStart: TunCleanup.Devices());
                DaemonControl.ClearRunning(Ceho.Root);
            }
            return 0;
        }

        if (NodeProbe.TunnelIsUp(cfg.TunAddress)
            || NodeProbe.TunnelIsUp(CehoConfig.GuardTunAddress)
            || DaemonControl.RunningPid(Ceho.Root) is not null)
        {
            if (OperatingSystem.IsWindows() && Os.IsElevated())
                DaemonControl.StopInstalledWindowsDaemons(Ceho.Root);
            TunCleanup.KillTunnelGuard(Ceho.Root, Console.WriteLine);
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Console.WriteLine);
            TunCleanup.ReleaseOurs(
                Ceho.RuntimeConfigPath, cfg.TunAddress, Ceho.Root, Console.WriteLine,
                attempts: 5, aggressive: true, beforeStart: TunCleanup.Devices());
            DaemonControl.ClearRunning(Ceho.Root);
            Console.WriteLine(Cli.S(cfg, "stop_cleaned"));
            return 0;
        }

        LeakGuard.SetTunnelGuard(Ceho.Root, false);
        Console.Error.WriteLine(Cli.S(cfg, "state_off"));
        return 1;
    }

    case "restart":
    {
        var cfg = CehoConfig.Load(Ceho.ConfigPath);

        if (Environment.GetEnvironmentVariable(Ceho.ViaDaemonVariable) != "1"
            && Os.IsElevated()
            && DaemonControl.IsRunning(Ceho.Root)
            && Auth.ReadPanelPointer(Ceho.Root) is not null
            && NodeProbe.TunnelIsUp(cfg.TunAddress)
            && await Cli.RunRemoteAsync(Ceho.Root, [Cli.ReloadCommand], cfg.Language) == 0)
            return 0;

        try { await Ceho.ApplyAsync(); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); }

        if (Environment.GetEnvironmentVariable(Ceho.ViaDaemonVariable) == "1")
        {
            File.WriteAllText(Ceho.RestartRequestPath, "");
            Console.WriteLine(Cli.S(cfg, "rules_applied"));
            return 0;
        }

        if (Autostart.IsEnabled())
        {
            if (!Os.IsElevated())
            {
                Console.Error.WriteLine(Cli.S(cfg, "rules_restart_needed", Os.IsWindows ? "" : "sudo "));
                return 1;
            }
            Autostart.StopService();
            if (!DaemonControl.WaitForExit(Ceho.Root, 20000))
            {
                // Автозапуск могли включить, пока daemon был запущен вручную.
                // systemctl/schtasks такой процесс не знает, поэтому передаём
                // управление службе через обычный сигнал остановки daemon.
                DaemonControl.RequestStop(Ceho.Root);
                DaemonControl.WaitForExit(Ceho.Root, 20000);
            }
            await Task.Delay(1000);
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, _ => {});
            TunCleanup.ReleaseOurs(
                Ceho.RuntimeConfigPath, cfg.TunAddress, Ceho.Root, _ => {},
                attempts: 5, aggressive: true, beforeStart: TunCleanup.Devices());
            await Task.Delay(3000);
            Autostart.Restart();
            var deadline = Environment.TickCount64 + 90000;
            while (Environment.TickCount64 < deadline)
            {
                await Task.Delay(2000);
                if (NodeProbe.TunnelIsUp(cfg.TunAddress)) break;
            }
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
            TunCleanup.ReleaseOurs(
                Ceho.RuntimeConfigPath, cfg.TunAddress, Ceho.Root, _ => {},
                attempts: 5, aggressive: true, beforeStart: TunCleanup.Devices());
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

}

if (cmd is "daemon" or "web")
{
    if (cmd == "daemon" && Environment.GetEnvironmentVariable(DaemonControl.ForegroundEnv) == "1")
        Os.DetachFromControllingTerminal();

    // Acquire before cleanup, listeners or PID publication. A concurrent launch
    // reuses the existing controller instead of creating a second tunnel owner.
    using var controllerLease = ControllerLease.TryAcquire(Ceho.Root);
    if (controllerLease is null || (DaemonControl.RunningPid(Ceho.Root) is { } existingPid
        && existingPid != Environment.ProcessId))
    {
        var panelPort = Auth.ReadPanelPointer(Ceho.Root) ?? cfg0.WebPort;
        var panelUrl = $"http://127.0.0.1:{panelPort}";
        Console.WriteLine(Strings.T(cfg0.Language, "panel_at", panelUrl));
        if (cmd == "web" && Assistant.Interactive) Os.OpenInBrowser(panelUrl);
        return 0;
    }

    DaemonControl.ListenForStop();
    var cfg = CehoConfig.Load(Ceho.ConfigPath);
    var withTunnel = cmd == "daemon";

    // Демон живёт без консоли (служба, задача планировщика, launchd), поэтому всё,
    // что он рассказывает, обязано попадать в файл журнала, а не только в stderr.
    Log.EchoToConsole = true;

    SingBoxProcess? proc = null;
    CehoConfig? activeEngineConfig = null;
    var adoptEngineOnce = true;
    var waitingForSetup = false;
    DaemonControl.ClearKeepEngine(Ceho.Root);
    DaemonControl.ClearStoppedByUser(Ceho.Root);
    Autostart.EnsureKeepEngineDropIn();
    var engineAdopted = false;
    var admittedConfiguration = new AdmittedConfiguration(cfg);
    SingBoxProcess? guard = null;
    var guardConfigPath = TunCleanup.GuardConfigPath(Ceho.Root);
    var shuttingDown = false;
    string? lastError = null;
    var wanted = false;
    var stopRequested = false;
    var recovery = new RecoveryPolicy();
    var guardRecovery = new RecoveryPolicy();
    Action runtimeChanged = () => { };
    Action<CehoConfig> rulesApplied = _ => { };
    string? exitCountry = null, exitIp = null;
    var probed = false;
    string? boundAddress = null;
    var tunnelMissing = 0;
    var failedExitChecks = 0;
    long exitCheckGeneration = -1;

    void StopGuard()
    {
        if (guard is null) return;
        guard.Stop(5000);
        guard.Dispose();
        guard = null;
        LeakGuard.SetTunnelGuard(Ceho.Root, false);
    }

    async Task StartGuard()
    {
        if (Os.IsWindows || guard is not null) return;
        if (!withTunnel || shuttingDown || proc is not null)
        {
            LeakGuard.SetTunnelGuard(Ceho.Root, false);
            return;
        }

        var c = admittedConfiguration.Snapshot();
        if (!c.FailClosed || !c.Apps.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder)))
        {
            LeakGuard.SetTunnelGuard(Ceho.Root, false);
            return;
        }

        if (!Os.IsElevated() || Os.ResolveSingBox(Ceho.Root) is null || guardRecovery.Exhausted) return;
        if (!guardRecovery.Wanted) guardRecovery.StartByUser();
        if (guardRecovery.Pending is not null && !guardRecovery.TryBegin(Environment.TickCount64, out _)) return;
        try
        {
            TunCleanup.KillOurProcesses(guardConfigPath, Log.Info);
            PrivateFile.Write(guardConfigPath, SingBoxConfigGenerator.GenerateFailClosed(c));
            Auth.RestrictConfigAccess(guardConfigPath);

            var p = new SingBoxProcess();
            p.Start(Ceho.SingBoxPath, guardConfigPath, Ceho.Root);
            await Task.Delay(700);
            if (!p.IsRunning)
            {
                var reason = p.Explain(c.Language);
                p.Dispose();
                LeakGuard.SetTunnelGuard(Ceho.Root, false);
                ScheduleGuardRecovery(reason);
                return;
            }

            guard = p;
            guardRecovery.Started(Environment.TickCount64);
            LeakGuard.SetTunnelGuard(Ceho.Root, true);
            Log.Info(Strings.T(c.Language, "guard_on"));
        }
        catch (Exception ex)
        {
            LeakGuard.SetTunnelGuard(Ceho.Root, false);
            ScheduleGuardRecovery(ex.Message);
        }
    }

    void ScheduleGuardRecovery(string reason)
    {
        if (guardRecovery.Exhausted) return;
        if (!guardRecovery.Wanted) guardRecovery.StartByUser();
        guardRecovery.Schedule(guardRecovery.Generation, "guard-failed", reason, Environment.TickCount64);
        Log.Warn(Strings.T(cfg.Language, "guard_failed", reason));
        if (guardRecovery.Exhausted)
        {
            lastError = Strings.T(cfg.Language, "guard_failed",
                RecoveryPolicy.TerminalMessage(cfg.Language, guardRecovery.MaxAttempts, reason));
            runtimeChanged();
            Log.Warn(lastError);
        }
    }

    // Кнопку «Включить», сторож и доктор нельзя пускать в движок одновременно —
    // очередь общая на демон и «chp doctor fix» (именованный Mutex).
    bool EngineAdapterStuck(string reason) =>
        reason.Contains("already exists", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("not ready", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("device is not ready", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("not functioning", StringComparison.OrdinalIgnoreCase)
        || (Os.IsWindows && reason.Contains("configure tun interface", StringComparison.OrdinalIgnoreCase));

    long cleanedAt = 0;
    IReadOnlyList<string>? cleanedDevices = null;

    void EnableAutostartOnFirstStart()
    {
        try
        {
            var current = CehoConfig.Load(Ceho.ConfigPath);
            if (current.AutostartOffered) return;
            current.AutostartOffered = true;
            current.Save(Ceho.ConfigPath);
            if (Autostart.IsEnabled()) return;
            var err = Autostart.Enable(Ceho.OwnExecutablePath, Ceho.Root);
            Log.Info(err is null ? "автозапуск включён при первом запуске защиты" : "автозапуск не включился: " + err);
        }
        catch (Exception ex) { Log.Info("автозапуск при первом запуске: " + ex.Message); }
    }

    async Task<string?> StartTunnel(IStageReport? report,
        string reasonCode = "manual-start", string? reason = null)
    {
        report?.StartupStep(1);
        report?.Phase(Strings.T(cfg.Language, "stage_engine_queue"), waiting: true);
        using (EngineMutex.Acquire(Ceho.Root))
            return await StartTunnelLocked(report, reasonCode, reason);
    }

    async Task<string?> StartTunnelLocked(IStageReport? report,
        string reasonCode = "manual-start", string? triggerReason = null, bool automatic = false, bool preserveAdmitted = false)
    {
        if (shuttingDown) return Strings.T(cfg.Language, "stage_stopping");
        if (automatic && (!wanted || !recovery.Wanted)) return null;
        if (!automatic)
        {
            DaemonControl.ClearStoppedByUser(Ceho.Root);
            recovery.StartByUser();
            guardRecovery.StartByUser();
        }
        wanted = true;
        // Another queued start may have finished while we waited. It is success,
        // not a new start and not an error for the caller that joined it.
        if (proc is { IsRunning: true }) return null;
        if (proc is not null) StopTunnelLocked();
        runtimeChanged();
        var connection = ReconnectHistory.Shared.Begin(reasonCode,
            triggerReason ?? Strings.T(cfg.Language, "reconnect_manual_start"), report);
        report = connection;
        var succeeded = false;
        CehoConfig? appliedRulesConfig = null;
        report?.StartupStep(1);
        DaemonControl.MarkStarting(Ceho.Root, recovering: lastError is not null);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var keepAdmitted = automatic || preserveAdmitted;
            var c = admittedConfiguration.ForStart(keepAdmitted, () => CehoConfig.Load(Ceho.ConfigPath));
            // Recheck every attempt, including starts requested through the panel.
            // Never download subscriptions or spawn probes with known blockers.
            c.Validate();
            var configurationError = StartupPreflight.ConfigurationError(c);
            if (configurationError is not null) throw new InvalidOperationException(configurationError);
            var hasEngine = Os.ResolveSingBox(Ceho.Root) is not null;
            var blockers = Preflight.Run(c, Ceho.Root).Where(check => check.Level == Preflight.Level.Blocker
                && check.Repair is not (Repair.PanelPort or Repair.ProxyPort)
                // The engine is present: this one dependency has an existing,
                // bounded download path below, after discovering whether it is needed.
                && !(hasEngine && check.Repair == Repair.Engine
                    && check.Title == Strings.T(c.Language, "pf_cronet_missing"))).ToArray();
            if (blockers.Length > 0)
                throw new InvalidOperationException(string.Join(" ", blockers.Select(check => check.Title + " " + check.Fix)));
            int? keepEngine = null;
            if (adoptEngineOnce)
            {
                adoptEngineOnce = false;
                keepEngine = TunCleanup.OurEnginePid(Ceho.RuntimeConfigPath);
            }
            engineAdopted = false;
            var leftover = keepEngine is null ? TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Log.Info) : 0;
            if (leftover > 0) await Task.Delay(500);
            if (keepEngine is null && Preflight.TryMoveProxyPortIfBusy(c, out var busyPort, out var freePort))
            {
                // An automatic retry must never overwrite newer deferred settings.
                if (!keepAdmitted) c.Save(Ceho.ConfigPath);
                Log.Info(Strings.T(c.Language, "proxy_port_moved", busyPort, freePort));
            }
            if (c.MixedPort == c.ClashApiPort || (keepEngine is null && (Preflight.TcpPortTaken(c.MixedPort) == true
                || Preflight.TcpPortTaken(c.ClashApiPort) == true)))
                throw new InvalidOperationException(c.Language == "ru"
                    ? "Порт прокси или API занят. Проверьте настройки сети; другие процессы не остановлены."
                    : "The proxy or API port is occupied. Check network settings; other processes were not stopped.");
            // Only this admitted start may change network protection. Observers use its snapshot.
            admittedConfiguration.Admit(c);
            LeakGuard.Apply(c, Ceho.Root);

            var nodes = await Ceho.LoadAllNodesAsync(c, preferCache: reasonCode != "exit-unavailable", report);

            if (nodes.Any(n => n.Protocol == ProxyProtocol.Naive) && Installer.MissingCronetDll(Ceho.Root))
            {
                report?.Phase(Strings.T(c.Language, "stage_cronet_fetch"), waiting: true);
                try
                {
                    await Installer.EnsureCronetAsync(Ceho.Root, m => report?.Note(m), c.Language);
                }
                catch (Exception ex)
                {
                    lastError = Strings.T(c.Language, "inst_engine_failed", ex.Message);
                    return lastError;
                }
            }

            report?.Phase(Strings.T(c.Language, "stage_writing_rules"));
            // Freeze the admitted routing input across subscription waits. A later saved
            // edit is still pending, including during service-start without a panel job.
            c = admittedConfiguration.Snapshot();
            // Revalidate the admitted snapshot before generating its rules.
            c.Validate();
            configurationError = StartupPreflight.ConfigurationError(c);
            if (configurationError is not null) throw new InvalidOperationException(configurationError);
            var generatedRules = SingBoxConfigGenerator.GenerateForConfig(nodes, c);
            if (keepEngine is not null)
            {
                string? previousRules = null;
                try { previousRules = File.ReadAllText(Ceho.RuntimeConfigPath); } catch { }
                if (previousRules != generatedRules)
                {
                    Log.Info("правила изменились: работающий движок будет перезапущен");
                    keepEngine = null;
                }
            }
            PrivateFile.Write(Ceho.RuntimeConfigPath, generatedRules);
            VerifiedConfigStore.Candidate? candidate = null;
            try { candidate = VerifiedConfigStore.Capture(Ceho.Root, c, generatedRules); }
            catch { Log.Warn("Не удалось подготовить проверенную копию настроек."); }
            admittedConfiguration.Admit(c);
            LeakGuard.Apply(c, Ceho.Root);
            Log.Info($"этап: подписки и правила {watch.Elapsed.TotalSeconds:F1} с");
            foreach (var networkLine in Os.DescribePhysicalNetwork(c.TunAddress)) Log.Info(networkLine);

            var reason = await BringEngineUp(c, report, keepEngine);
            for (var attempt = 1;
                 reason is not null
                 && EngineAdapterStuck(reason)
                 && !Volatile.Read(ref stopRequested)
                 && attempt < 5;
                 attempt++)
            {
                var retryMessage = Strings.T(c.Language, "stage_adapter_retry", attempt + 1, 5, reason!);
                connection.Retrying(attempt + 1, 5, retryMessage);
                Log.Info(retryMessage);
                DaemonControl.MarkStarting(Ceho.Root, recovering: true);
                var beforeRetry = TunCleanup.Devices();
                TunCleanup.ReleaseOurs(
                    Ceho.RuntimeConfigPath, c.TunAddress, Ceho.Root, Log.Info,
                    attempts: 5, aggressive: true, beforeStart: beforeRetry);
                var tunName = TunCleanup.AdapterName(attempt + 1);
                generatedRules = SingBoxConfigGenerator.GenerateForConfig(nodes, c, tunName);
                PrivateFile.Write(Ceho.RuntimeConfigPath, generatedRules);
                candidate = null;
                try { candidate = VerifiedConfigStore.Capture(Ceho.Root, c, generatedRules); }
                catch { Log.Warn("Не удалось подготовить проверенную копию настроек."); }
                Log.Info($"пробую другое имя адаптера: {tunName}");
                await Task.Delay(TimeSpan.FromSeconds(4));
                reason = await BringEngineUp(c, report);
            }

            if (reason is not null)
            {
                lastError = reason;
                await StartGuard();
                return reason;
            }

            lastError = null;
            recovery.Started(Environment.TickCount64, healthy: false);
            if (candidate is not null)
            {
                try { VerifiedConfigStore.CommitVerified(Ceho.Root, candidate); }
                catch { Log.Warn("Не удалось сохранить проверенную копию настроек."); }
            }
            probed = false;
            boundAddress = Os.PhysicalBindAddress(c.TunAddress)?.ToString();
            if (!engineAdopted)
            {
                report?.Phase(Strings.T(c.Language, "stage_bounce_apps"));
                watch.Restart();
                var bounced = IsolatedAppBounce.ResetNetwork(c, Log.Info);
                Log.Info($"этап: сброс соединений программ {watch.Elapsed.TotalSeconds:F1} с");
                if (bounced.Killed + bounced.Connections > 0)
                    Log.Info(
                        $"сброшены старые соединения {string.Join(", ", bounced.Labels)}: " +
                        $"процессы {bounced.Killed}, TCP {bounced.Connections}");
            }
            appliedRulesConfig = c;
            succeeded = true;
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("защита не включилась", ex);
            lastError = ex.Message;
            await StartGuard();
            return ex.Message;
        }
        finally
        {
            if (proc is null && wanted)
                ScheduleRecoveryLocked("retry-after-failure", lastError ?? Strings.T(cfg.Language, "start_failed"));
            else DaemonControl.ClearStarting(Ceho.Root);
            runtimeChanged();
            if (succeeded && appliedRulesConfig is not null) rulesApplied(appliedRulesConfig);
            connection.Complete(succeeded, succeeded
                ? Strings.T(cfg.Language, "state_on")
                : lastError ?? Strings.T(cfg.Language, "start_failed"));
        }
    }

    void ScheduleRecoveryLocked(string reasonCode, string reason)
    {
        if (shuttingDown || !wanted) return;
        recovery.Schedule(recovery.Generation, reasonCode, reason, Environment.TickCount64);
        if (recovery.Exhausted)
        {
            lastError = RecoveryPolicy.TerminalMessage(cfg.Language, recovery.MaxAttempts, reason);
            DaemonControl.ClearStarting(Ceho.Root);
            var terminal = ReconnectHistory.Shared.Begin("recovery-exhausted", lastError);
            terminal.Complete(false, lastError);
            Log.Warn(lastError);
        }
        else if (recovery.Pending is not null)
        {
            lastError = reason;
            DaemonControl.MarkStarting(Ceho.Root, recovering: true);
        }
    }

    async Task QueueAutomaticRecovery(string reasonCode, string reason, long generation)
    {
        using var gate = EngineMutex.Acquire(Ceho.Root);
        // A slow pre-sleep/network probe must not restart a newer session or
        // undo a stop that was accepted while it was awaiting the network.
        if (shuttingDown || !wanted || !recovery.IsCurrent(generation)) return;
        StopTunnelLocked();
        ScheduleRecoveryLocked(reasonCode, reason);
        await StartGuard();
    }

    async Task<string?> BringEngineUp(CehoConfig c, IStageReport? report, int? keepEngine = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (keepEngine is int keptPid)
        {
            report?.Phase(Strings.T(c.Language, "stage_engine_wait"), waiting: true);
            var kept = new SingBoxProcess();
            try
            {
                kept.Attach(keptPid);
                var keptReady = await EngineReadiness.WaitAsync(
                    () => kept.IsRunning,
                    () => SingBoxProcess.ListensAsync(c.MixedPort, c.ClashApiPort));
                if (keptReady == EngineReadinessResult.Ready && kept.IsRunning)
                {
                    Log.Info($"движок pid {keptPid} подхвачен без перезапуска, туннель не прерывался");
                    proc = kept;
                    activeEngineConfig = c;
                    engineAdopted = true;
                    return null;
                }
            }
            catch (Exception ex) { Log.Warn($"движок pid {keptPid} подхватить не удалось: {ex.Message}"); }
            Log.Info("подхваченный движок не отвечает: запускаю заново");
            kept.Dispose();
            await Task.Delay(500);
        }

        report?.Phase(Strings.T(c.Language, "stage_cleanup"));

        var handover = guard is not null;
        StopGuard();

        var before = TunCleanup.Devices();
        var justCleaned = cleanedDevices is not null
            && Environment.TickCount64 - cleanedAt < 60000
            && before.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
                cleanedDevices.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
            && !TunCleanup.IsOurEngineRunning(Ceho.RuntimeConfigPath, Ceho.Root);
        cleanedDevices = null;
        if (!handover && justCleaned)
        {
            Log.Info("уборка перед запуском не нужна: только что убрано при остановке");
            TunCleanup.PrepareWintunForStart(Log.Info);
        }
        else if (!handover)
        {
            // Движок от упавшего прошлого сеанса нам не сын: демон его не убьёт, уходя,
            // а порт прокси он держит — и новый запуск падает на «адрес уже занят».
            var killed = TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Log.Info);
            if (killed > 0) await Task.Delay(500);
            TunCleanup.ReleaseOurs(
                Ceho.RuntimeConfigPath, c.TunAddress, Ceho.Root, Log.Info,
                attempts: 3, aggressive: true, beforeStart: before);
            TunCleanup.RemoveLeftovers(Log.Info, c.TunAddress, Ceho.Root, before, Ceho.RuntimeConfigPath);
            TunCleanup.PrepareWintunForStart(Log.Info);
            await Task.Delay(1500);
        }
        Log.Info($"этап: подготовка к запуску {watch.Elapsed.TotalSeconds:F1} с");
        watch.Restart();

        report?.StartupStep(2);
        report?.Phase(Strings.T(c.Language, "stage_engine_start"));

        Os.AdoptOwnEngine(Ceho.Root);
        Auth.RestrictConfigAccess(Ceho.ConfigPath);
        var p = new SingBoxProcess();
        try
        {
            p.Start(Ceho.SingBoxPath, Ceho.RuntimeConfigPath, Ceho.Root);
            Log.Info($"движок запущен, pid {p.ProcessId}");

            report?.StartupStep(3);
            report?.Phase(Strings.T(c.Language, "stage_engine_wait"), waiting: true);
            var readiness = await EngineReadiness.WaitAsync(
                () => p.IsRunning,
                () => SingBoxProcess.ListensAsync(c.MixedPort, c.ClashApiPort));
            Log.Info($"этап: запуск движка {watch.Elapsed.TotalSeconds:F1} с, готовность: {readiness}");
            TunCleanup.Remember(Ceho.Root, c.TunAddress, Log.Info, before);
            if (readiness == EngineReadinessResult.Ready && p.IsRunning)
            {
                proc = p;
                activeEngineConfig = c;
                return null;
            }

            // A still-running process with missing listeners is a failed startup too.
            var reason = readiness == EngineReadinessResult.TimedOut
                ? Strings.T(c.Language, "engine_ready_timeout")
                : p.Explain(c.Language);
            Log.Error($"движок не устоял: {reason}");
            p.Dispose();
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Log.Info);
            if (EngineAdapterStuck(reason))
            {
                TunCleanup.ReleaseOurs(
                    Ceho.RuntimeConfigPath, c.TunAddress, Ceho.Root, Log.Info,
                    attempts: 5, aggressive: true, beforeStart: before);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            else
            {
                TunCleanup.RemoveLeftovers(Log.Info, c.TunAddress, Ceho.Root, before, Ceho.RuntimeConfigPath);
            }
            return reason;
        }
        catch
        {
            p.Dispose();
            throw;
        }
    }

    string? StopTunnel()
    {
        using (EngineMutex.Acquire(Ceho.Root))
            return StopTunnelLocked();
    }

    string? StopTunnelLocked()
    {
        if (proc is null) return Strings.T(cfg.Language, "already_off");
        var stoppingConfig = activeEngineConfig ?? cfg;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var clean = proc.Stop(Os.IsWindows ? 2000 : 8000);
        Log.Info($"этап: остановка движка {watch.Elapsed.TotalSeconds:F1} с");
        watch.Restart();
        proc.Dispose();
        proc = null;
        activeEngineConfig = null;
        runtimeChanged();
        exitCountry = exitIp = null;
        probed = false;

        if (!clean)
        {
            Log.Info("движок не успел завершиться сам, завершаю принудительно и снимаю следы");
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, Log.Info);
            Thread.Sleep(800);
        }

        TunCleanup.ReleaseOurs(
            Ceho.RuntimeConfigPath, stoppingConfig.TunAddress, Ceho.Root, Log.Info,
            attempts: 5, aggressive: true, beforeStart: TunCleanup.Devices());
        if (Os.IsWindows && TunCleanup.LastReleaseClean)
            (cleanedAt, cleanedDevices) = (Environment.TickCount64, TunCleanup.Devices());
        Log.Info($"этап: уборка после остановки {watch.Elapsed.TotalSeconds:F1} с");
        StartGuard().GetAwaiter().GetResult();
        return null;
    }

    var web = new WebServer(
        Ceho.ConfigPath,
        () => new WebServer.ControlState(proc is not null, exitCountry, exitIp, lastError, probed),
        Log.Info);
    runtimeChanged = web.NotifyEngineStateChanged;
    rulesApplied = web.NotifyRulesApplied;

    async Task<string?> RestartTunnel(IStageReport? report,
        string reasonCode = "manual-restart", string? reason = null, bool onlyIfWanted = false)
    {
        report?.Phase(Strings.T(cfg.Language, "stage_engine_queue"), waiting: true);
        // Keep stop and start in the same critical section. A stop request cannot
        // slip between them and accidentally be undone by this restart.
        using var gate = EngineMutex.Acquire(Ceho.Root);
        if (shuttingDown || (onlyIfWanted && !wanted)) return null;
        report?.Phase(Strings.T(cfg.Language, "stage_stopping"));
        StopTunnelLocked();
        return await StartTunnelLocked(report, reasonCode,
            reason ?? Strings.T(cfg.Language, "reconnect_manual_restart"));
    }

    web.OnStart = async report =>
    {
        var err = await StartTunnel(report);
        if (err is null) EnableAutostartOnFirstStart();
        return err;
    };
    web.OnAppsLive = () =>
    {
        var c = CehoConfig.Load(Ceho.ConfigPath);
        var engine = EngineConnections.Fetch(c.ClashApiPort);
        return c.Apps.Where(a => a.Enabled).Select(a =>
        {
            var v = Ceho.VerifyApp(a, c.TunAddress, c.Language);
            var (vpn, direct) = EngineConnections.CountFor(a, engine);
            return new WebServer.AppLive(a.Folder, v.Processes, v.Tunneled, v.Direct, vpn, direct);
        }).ToList();
    };
    web.OnStop = () =>
    {
        Volatile.Write(ref stopRequested, true);
        using var gate = EngineMutex.Acquire(Ceho.Root);
        wanted = false;
        recovery.StopByUser();
        guardRecovery.StartByUser();
        lastError = null;
        runtimeChanged();
        DaemonControl.ClearStarting(Ceho.Root);
        DaemonControl.MarkStoppedByUser(Ceho.Root);
        var stopError = StopTunnelLocked();
        Volatile.Write(ref stopRequested, false);
        return Task.FromResult(stopError);
    };
    web.OnRemoveLastApp = removed =>
    {
        using var gate = EngineMutex.Acquire(Ceho.Root);
        wanted = false;
        recovery.StopByUser();
        guardRecovery.StopByUser();
        admittedConfiguration.Admit(removed);
        if (proc is not null) StopTunnelLocked();
        StopGuard();
        var guardError = LeakGuard.Apply(removed, Ceho.Root);
        lastError = guardError;
        runtimeChanged();
        DaemonControl.ClearStarting(Ceho.Root);
        return Task.FromResult(guardError);
    };
    web.OnRestart = report => RestartTunnel(report);
    web.OnRestoreVerified = async (acknowledgeSecurity, expectedVerifiedUtc, report) =>
    {
        report.Phase(Strings.T(cfg.Language, "stage_engine_queue"), waiting: true);
        using var gate = EngineMutex.Acquire(Ceho.Root);
        if (shuttingDown) throw new InvalidOperationException(Strings.T(cfg.Language, "stage_stopping"));
        var wasRunning = proc is not null || wanted;
        report.Phase(Strings.T(cfg.Language, "stage_restore_verified"));
        VerifiedConfigStore.Restore(Ceho.Root, acknowledgeSecurity, expectedVerifiedUtc);
        var restored = CehoConfig.Load(Ceho.ConfigPath);
        admittedConfiguration.Admit(restored);
        if (!wasRunning)
        {
            // Refresh only an already-active fail-closed guard; restoring settings
            // must not turn an intentionally disabled working tunnel on.
            if (guard is not null)
            {
                StopGuard();
                await StartGuard();
            }
            return Strings.T(restored.Language, "restore_verified_off");
        }

        recovery.StartByUser();
        guardRecovery.StartByUser();
        runtimeChanged();
        report.StartupStep(1);
        var connection = ReconnectHistory.Shared.Begin("verified-config-restored",
            Strings.T(restored.Language, "reconnect_verified_config"), report);
        try
        {
            connection.Phase(Strings.T(restored.Language, "stage_stopping"));
            StopTunnelLocked();
            LeakGuard.Apply(restored, Ceho.Root);
            var error = await BringEngineUp(restored, connection);
            if (error is not null) throw new InvalidOperationException(error);
            lastError = null;
            recovery.Started(Environment.TickCount64, healthy: false);
            runtimeChanged();
            exitCountry = exitIp = null;
            probed = false;
            boundAddress = Os.PhysicalBindAddress(restored.TunAddress)?.ToString();
            connection.Phase(Strings.T(restored.Language, "stage_bounce_apps"));
            IsolatedAppBounce.ResetNetwork(restored, Log.Info);
            var result = Strings.T(restored.Language, "restore_verified_on");
            connection.Complete(true, result);
            DaemonControl.ClearStarting(Ceho.Root);
            return result;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            connection.Complete(false, ex.Message);
            await StartGuard();
            if (proc is null && wanted) ScheduleRecoveryLocked("retry-after-failure", ex.Message);
            runtimeChanged();
            throw;
        }
    };
    // Служба поднялась без программ и ждёт настройки: как только препятствий нет, защиту включаем сама.
    async Task<string?> StartIfWaitingForSetup(IStageReport? report)
    {
        if (!waitingForSetup || proc is not null || shuttingDown) return null;
        var blocked = Preflight.Run(CehoConfig.Load(Ceho.ConfigPath), Ceho.Root)
            .Any(c => c.Level == Preflight.Level.Blocker && c.Repair is not (Repair.PanelPort or Repair.ProxyPort));
        if (blocked) return null;
        waitingForSetup = false;
        return await StartTunnel(report, "setup-complete", Strings.T(cfg.Language, "reconnect_service_start"));
    }

    web.OnApply = async report =>
    {
        var applied = await ApplyRules(true, report);
        if (await StartIfWaitingForSetup(report) is { } startError) throw new InvalidOperationException(startError);
        return proc is not null ? Strings.T(cfg.Language, "state_on") : applied;
    };
    Task<string> ApplyRules(bool allowRestart, IStageReport report) => TunnelRuleApply.RunAsync(
        report,
        () =>
        {
            report.Phase(Strings.T(cfg.Language, "stage_engine_queue"), waiting: true);
            return EngineMutex.Acquire(Ceho.Root);
        },
        () => proc is not null,
        async p =>
        {
            p.Phase(Strings.T(cfg.Language, "stage_stopping"));
            StopTunnelLocked();
            return await StartTunnelLocked(p, "rules-changed", Strings.T(cfg.Language, "reconnect_rules"));
        },
        async p =>
        {
            var admitted = CehoConfig.Load(Ceho.ConfigPath);
            admittedConfiguration.Admit(admitted);
            var result = await Ceho.ApplyConfigurationAsync(admitted, p);
            // Off-tunnel apply is explicit too. Refresh an existing Unix fail-closed guard.
            if (guard is not null) { StopGuard(); await StartGuard(); }
            return result;
        },
        Strings.T(cfg.Language, "rules_applied"), allowRestart);
    web.OnApplyWithRestartConfirmation = ApplyRules;
    web.WrappedNames = Cli.Wrapped;
    web.OnCheckSubs = async report =>
    {
        var c = CehoConfig.Load(Ceho.ConfigPath);
        var nodes = await Ceho.LoadAllNodesAsync(c, preferCache: false, report);
        return Strings.T(c.Language, "sub_checked_nodes", nodes.Count);
    };

    async Task<string> UpdateEngineAsync(IStageReport report)
    {
        var c = CehoConfig.Load(Ceho.ConfigPath);
        string S(string key, params object[] a) => Strings.T(c.Language, key, a);

        var current = Os.ResolveSingBox(Ceho.Root);
        var installed = current is null ? null : Installer.EngineVersionOf(current);
        if (installed is not null && !Installer.EngineOutdated(installed)) return S("engine_current", installed);

        try
        {
            report.Stage(S("job_engine_update"), 10);
            var fresh = await Installer.PrepareEngineUpdateAsync(
                Ceho.Root, Ceho.RuntimeConfigPath, m => report.Note(m), c.Language);

            report.Stage(S("engine_stage_swap"), 75);
            using (EngineMutex.Acquire(Ceho.Root))
            {
                var wasRunning = proc is not null;
                if (wasRunning) StopTunnelLocked();
                Installer.SwapEngine(Ceho.Root, fresh);
                if (wasRunning && await StartTunnelLocked(report, "engine-update", S("reconnect_engine_update"), preserveAdmitted: true) is { } error)
                {
                    Installer.RestoreEngine(Ceho.Root);
                    await StartTunnelLocked(report, "engine-rollback", S("reconnect_engine_rollback"), preserveAdmitted: true);
                    throw new InvalidOperationException(S("engine_rolled_back", error));
                }
            }
            return S("engine_updated", Installer.EngineVersion);
        }
        finally
        {
            Installer.DropEngineStaging(Ceho.Root);
        }
    }

    web.OnEngineUpdate = UpdateEngineAsync;

    async Task<string> UpdateAsync(bool install, IStageReport report)
    {
        var c = CehoConfig.Load(Ceho.ConfigPath);
        report.Stage(Strings.T(c.Language, "job_update_check"), 20);
        var release = await Updater.CheckAsync(c.UpdateRepo);
        if (release is null) return Strings.T(c.Language, "upd_none");
        if (!install) return Strings.T(c.Language, "upd_found", release.Version);

        var binaryDir = Path.GetDirectoryName(Ceho.OwnExecutablePath) ?? Ceho.Root;
        if (!Preflight.FolderIsWritable(binaryDir, out var noWrite))
            throw new InvalidOperationException(
                Strings.T(c.Language, "upd_no_write", Ceho.OwnExecutablePath, noWrite));

        var swept = Installer.SweepOldBinaries(binaryDir, Ceho.OwnExecutablePath);
        if (swept > 0) report.Note(Strings.T(c.Language, "upd_swept", swept));

        report.Stage(Strings.T(c.Language, "stage_download",
            release.Version, release.Size / 1024 / 1024), 40);
        UpdateRollback.RememberProtection(Ceho.Root, proc is not null);
        var (downloaded, shut) = await Updater.DownloadThenPrepareForUpdateAsync(
            async () =>
            {
                var staged = await Updater.DownloadAsync(
                    release, Ceho.OwnExecutablePath, m => report.Note(m));
                if (Installer.MissingCronetDll(Ceho.Root))
                {
                    try
                    {
                        report.Note(Strings.T(c.Language, "engine_cronet_missing", Ceho.Root));
                        await Installer.DownloadEngineAsync(Ceho.Root, m => report.Note(m), c.Language);
                    }
                    catch (Exception ex)
                    {
                        report.Note(Strings.T(c.Language, "inst_engine_failed", ex.Message));
                    }
                }
                return staged;
            },
            () =>
            {
                report.Stage(Strings.T(c.Language,
                    DaemonControl.CanKeepEngine && proc is { IsRunning: true } ? "upd_keeping_tun" : "upd_stopping_tun"), 80);
                return TunnelShutdown.PrepareFromRunningDaemonAsync(
                    c, Ceho.Root, Ceho.RuntimeConfigPath, () => { StopTunnel(); },
                    async () => { await StartTunnel(null); }, m => report.Note(m),
                    keepEngine: DaemonControl.CanKeepEngine && proc is { IsRunning: true });
            });
        if (!shut.Ok)
            throw new InvalidOperationException(Strings.T(c.Language, shut.ErrorKey ?? "upd_need_reboot"));

        report.Stage(Strings.T(c.Language, "stage_installing"), 90);
        var handoffId = report is JobProgress jobProgress
            ? jobProgress.JobId
            : "web-" + Guid.NewGuid().ToString("N");

        if (OperatingSystem.IsWindows())
        {
            try
            {
                UpdateHandoff.Write(Ceho.Root, UpdateHandoff.Pending(handoffId, release.Version));
                report.Stage("Замена ожидает перезапуска службы; проверяю установленную версию.", 95);
                DaemonControl.SpawnUpdateRelaunchHelper(
                    Ceho.OwnExecutablePath, downloaded, Ceho.Root, release.Version, handoffId);
            }
            catch
            {
                try { if (File.Exists(downloaded)) File.Delete(downloaded); } catch { }
                try { UpdateHandoff.Write(Ceho.Root, UpdateHandoff.Failed(handoffId, release.Version)); } catch { }
                DaemonControl.ClearKeepEngine(Ceho.Root);
                try { await StartTunnel(null); } catch { }
                throw;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                Environment.Exit(0);
            });
            return "Замена ожидает перезапуска службы; панель покажет результат после проверки версии.";
        }

        var applied = Updater.ApplyDownloaded(
            downloaded, Ceho.OwnExecutablePath, release.Version, m => report.Note(m));
        if (!applied.Ok)
        {
            try { UpdateHandoff.Write(Ceho.Root, UpdateHandoff.Failed(handoffId, release.Version)); } catch { }
            DaemonControl.ClearKeepEngine(Ceho.Root);
            try { await StartTunnel(null); } catch { }
            throw new InvalidOperationException(
                Strings.T(c.Language, "upd_not_applied",
                    applied.Installed ?? Updater.CurrentVersion, release.Version)
                + " " + Strings.T(c.Language, "upd_finish_by_hand", "sudo chp update --yes"));
        }

        try { UpdateHandoff.Write(Ceho.Root, UpdateHandoff.Verified(handoffId, release.Version)); } catch { }
        report.Stage(Strings.T(c.Language, "upd_relaunch"), 95);

        var autostart = Autostart.IsEnabled();
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            if (autostart) Autostart.Restart();
            else DaemonControl.SpawnRelaunchHelper(Ceho.OwnExecutablePath, Ceho.Root);
            await Task.Delay(1500);
            Environment.Exit(0);
        });
        return Strings.T(c.Language, "upd_done", release.Version);
    }

    web.OnUpdate = UpdateAsync;

    web.OnElevate = () =>
    {
        if (Os.IsElevated()) return Task.FromResult<string?>(Strings.T(cfg.Language, "elevate_already"));
        var way = Elevation.Way();
        if (way == RightsAsk.None) return Task.FromResult<string?>(Strings.T(cfg.Language, "elevate_no_way"));
        var exe = Environment.ProcessPath ?? Ceho.OwnExecutablePath;
        _ = Task.Run(() => Elevation.Run(way, exe, new[] { "restart" }));
        return Task.FromResult<string?>(null);
    };

    bool RollBackAfterBadUpdate(string reason)
    {
        var exe = Ceho.OwnExecutablePath;
        var current = Updater.CurrentVersion;
        if (!UpdateRollback.ShouldRollBack(Ceho.Root, current, exe, DateTime.UtcNow)) return false;
        try
        {
            var candidate = UpdateRollback.PrepareCandidate(exe)!;
            var previous = Updater.ReadVersion(candidate);
            if (previous is null || previous == current) { File.Delete(candidate); return false; }

            UpdateRollback.Mark(Ceho.Root, current);
            var message = Strings.T(cfg.Language, "upd_rolled_back", current, previous, reason);
            Log.Error(message);
            var status = new UpdateHandoff.Status("rollback-" + Guid.NewGuid().ToString("N"), "failed", current, message);
            UpdateHandoff.Write(Ceho.Root, status);

            shuttingDown = true;
            StopTunnel();
            StopGuard();
            if (OperatingSystem.IsWindows())
            {
                DaemonControl.SpawnUpdateRelaunchHelper(exe, candidate, Ceho.Root, previous, status.JobId);
                Thread.Sleep(2500);
                Environment.Exit(0);
                return true;
            }

            var applied = Updater.ApplyDownloaded(candidate, exe, previous, Log.Info);
            if (!applied.Ok) { Log.Error(Strings.T(cfg.Language, "upd_rollback_failed")); return false; }
            UpdateHandoff.Write(Ceho.Root, status);
            if (Autostart.IsEnabled()) Autostart.Restart();
            else DaemonControl.SpawnRelaunchHelper(exe, Ceho.Root);
            Thread.Sleep(1500);
            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(Strings.T(cfg.Language, "upd_rollback_failed"), ex);
            return false;
        }
    }
    web.OnPool = report =>
        Ceho.LoadAllNodesAsync(CehoConfig.Load(Ceho.ConfigPath), preferCache: false, report);
    web.OnExit = () => Ceho.ProbeExitAsync(CehoConfig.Load(Ceho.ConfigPath).MixedPort);
    web.OnCountries = async report =>
    {
        var nodes = await Ceho.LoadAllNodesAsync(
            CehoConfig.Load(Ceho.ConfigPath), preferCache: false, report);
        report.Stage(Strings.T(cfg.Language, "country_probe"), 96);
        return await Ceho.RefreshNodeCountriesAsync(nodes, report, cfg.Language);
    };

    web.OnUninstall = () =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            shuttingDown = true;
            StopTunnel();
            StopGuard();
            Autostart.Purge();
            TunCleanup.KillOurProcesses(guardConfigPath, _ => {});
            TunCleanup.KillOurProcesses(Ceho.RuntimeConfigPath, _ => {});
            TunCleanup.KillOurProcesses(Installer.BinaryPath(Ceho.Root) + " daemon", _ => {});
            TunCleanup.RemoveLeftovers(_ => {}, cfg.TunAddress, Ceho.Root);
            DaemonControl.ClearRunning(Ceho.Root);
            await Ceho.DisposeCountryDatabaseAsync();
            try
            {
                foreach (var f in new[] { Ceho.ConfigPath, Ceho.RuntimeConfigPath, guardConfigPath })
                    if (File.Exists(f)) File.Delete(f);
                foreach (var f in Directory.GetFiles(Ceho.Root, "sub-*.txt")) File.Delete(f);
                var pointer = Path.Combine(Ceho.Root, "panel.port");
                if (File.Exists(pointer)) File.Delete(pointer);
            }
            catch { }
            Installer.RemoveRuntimeFiles(Ceho.Root);
            Installer.Remove(Ceho.Root, _ => {}, cfg.Language);
            foreach (var name in new[] { Os.EngineFileName, Os.SingBoxFileName, Installer.CronetFileName }.Distinct())
            {
                var file = Path.Combine(Ceho.Root, name);
                if (File.Exists(file))
                    try { File.Delete(file); } catch { }
            }
            if (Os.IsWindows)
            {
                try
                {
                    var binary = Installer.BinaryPath(Ceho.Root);
                    var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
                        $"/c ping 127.0.0.1 -n 3 >nul & del /f /q \"{binary}\" & del /f /q \"{Path.Combine(Ceho.Root, "chp.cmd")}\" & rmdir /q \"{Ceho.Root}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WorkingDirectory = Environment.SystemDirectory,
                    };
                    try { Environment.CurrentDirectory = Environment.SystemDirectory; } catch { }
                    System.Diagnostics.Process.Start(psi);
                }
                catch { }
            }
            else
            {
                try
                {
                    var self = Installer.BinaryPath(Ceho.Root);
                    if (File.Exists(self)) File.Delete(self);
                    if (Directory.Exists(Ceho.Root) && Directory.GetFileSystemEntries(Ceho.Root).Length == 0)
                        Directory.Delete(Ceho.Root);
                }
                catch { }
            }
            Environment.Exit(0);
        });
        return Task.FromResult(Strings.T(cfg.Language, "uninstall_done"));
    };

    web.OnApiCommand = async argv =>
    {
        if (argv is [Cli.ReloadCommand])
        {
            var language = CehoConfig.Load(Ceho.ConfigPath).Language;
            if (proc is null)
                try { await Task.Run(() => EngineMutex.Acquire(Ceho.Root).Dispose()); }
                catch (TimeoutException) { }
            if (proc is null)
            {
                if (await StartIfWaitingForSetup(new DelegateReport(Log.Info)) is { } startError) return (false, startError);
                return (true, proc is not null ? Strings.T(language, "state_on") : "");
            }
            var error = await RestartTunnel(new DelegateReport(Log.Info));
            return error is null ? (true, Strings.T(language, "rules_applied")) : (false, error);
        }

        if (argv.Length == 0 || !Cli.CanRunRemotely(argv[0]))
            return (false, Strings.T(cfg.Language, "remote_not_allowed", argv.Length > 0 ? argv[0] : ""));

        if (argv[0] is "engine" or "движок" && argv.Skip(1).Any(a => a is "update" or "обновить" or "--force"))
        {
            try { return (true, await UpdateEngineAsync(new DelegateReport(Log.Info))); }
            catch (Exception ex) { return (false, Strings.T(cfg.Language, "engine_update_failed", ex.Message)); }
        }

        if (argv[0] == "update")
        {
            var install = argv.Contains("--yes");
            try
            {
                var text = await UpdateAsync(install, new DelegateReport(Log.Info));
                if (!install && !text.StartsWith(Strings.T(cfg.Language, "upd_none"), StringComparison.Ordinal))
                    text += " " + Strings.T(cfg.Language, "upd_remote_needs_yes");
                return (true, text);
            }
            catch (Exception ex)
            {
                return (false, Strings.T(cfg.Language, "upd_failed", ex.Message));
            }
        }

        try { File.Delete(Ceho.RestartRequestPath); } catch { }
        var (ok, output) = await Task.Run(() =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Ceho.OwnExecutablePath)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            foreach (var a in argv) psi.ArgumentList.Add(a);
            psi.Environment[Ceho.ViaDaemonVariable] = "1";

            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return (false, "cannot start");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            return (p.ExitCode == 0, (stdout + stderr).Trim());
        });

        if (!File.Exists(Ceho.RestartRequestPath)) return (ok, output);
        try { File.Delete(Ceho.RestartRequestPath); } catch { }
        if (proc is null)
            try { await Task.Run(() => EngineMutex.Acquire(Ceho.Root).Dispose()); }
            catch (TimeoutException) { }
        if (proc is null) return (ok, output);
        var lang = CehoConfig.Load(Ceho.ConfigPath).Language;
        var restartError = await RestartTunnel(new DelegateReport(Log.Info));
        return restartError is null
            ? (ok, output + "\n" + Strings.T(lang, "rules_applied"))
            : (false, output + "\n" + restartError);
    };

    if (withTunnel && Os.IsElevated())
        try { Installer.AdoptSystemEngine(Ceho.Root, Log.Info, cfg.Language); }
        catch (Exception ex) { Log.Error("движок из системы перенести не удалось", ex); }

    if (withTunnel && Os.IsElevated())
        _ = Task.Run(() => TrayInstaller.EnsureAsync(Ceho.Root, cfg.UpdateRepo, true, Log.Info, cfg.Language));

    if (withTunnel) Ceho.DecideTunnelIpv6(cfg, Log.Info, Log.Warn);

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

    DaemonControl.MarkRunning(Ceho.Root);

    if (withTunnel)
    {
        await StartGuard();

        var tunnelBlockers = startupBlockers
            .Where(c => c.Repair is not (Repair.PanelPort or Repair.ProxyPort)).ToList();
        if (tunnelBlockers.Count > 0)
        {
            waitingForSetup = true;
            Console.Error.WriteLine(Strings.T(cfg.Language, "panel_only", cfg.WebPort));
        }
        else
        {
            var err = await StartTunnel(null, "service-start", Strings.T(cfg.Language, "reconnect_service_start"));
            Log.Info(err is null
                ? Strings.T(cfg.Language, "state_on")
                : $"{Strings.T(cfg.Language, "start_failed")}: {err}");
            if (err is not null && RollBackAfterBadUpdate(err)) return 0;
        }
    }

    Auth.RestrictConfigAccess(Ceho.ConfigPath);

    using var cts = new CancellationTokenSource();
    // Состояние процесса проверяем отдельно от сетевого probe: тот может ждать таймаут,
    // а упавший движок нужно поднимать сразу.
    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                if (proc is null && guard is not null && !guard.IsRunning)
                {
                    using (EngineMutex.Acquire(Ceho.Root))
                    {
                        if (proc is null && guard is not null && !guard.IsRunning)
                        {
                            var reason = guard.Explain(cfg.Language);
                            StopGuard();
                            ScheduleGuardRecovery(reason);
                            await StartGuard();
                        }
                    }
                }

                if (proc is null && guard is null && guardRecovery.Pending is not null)
                {
                    using var gate = EngineMutex.Acquire(Ceho.Root);
                    await StartGuard();
                }

                if (proc is not null && !proc.IsRunning)
                {
                    using var gate = EngineMutex.Acquire(Ceho.Root);
                    if (proc is not null && !proc.IsRunning)
                    {
                        var reason = proc.Explain(cfg.Language);
                        Log.Error($"{Strings.T(cfg.Language, "engine_gone")}: {reason}");
                        StopTunnelLocked();
                        ScheduleRecoveryLocked("engine-exited", Strings.T(cfg.Language, "reconnect_engine_exit", reason));
                    }
                }

                if (proc is null && wanted && recovery.Pending is not null)
                {
                    using var gate = EngineMutex.Acquire(Ceho.Root);
                    if (proc is null && wanted && recovery.TryBegin(Environment.TickCount64, out var retry))
                    {
                        var again = await StartTunnelLocked(null, retry!.ReasonCode, retry.Reason, automatic: true);
                        Log.Info(again is null ? Strings.T(cfg.Language, "state_on")
                            : $"{Strings.T(cfg.Language, "start_failed")}: {lastError ?? again}");
                    }
                }
            }
            catch (Exception ex) { Log.Error("сторож движка не смог выполнить проверку", ex); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), cts.Token); } catch { return; }
        }
    });

    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var observedGeneration = recovery.Generation;
                if (exitCheckGeneration != observedGeneration)
                {
                    exitCheckGeneration = observedGeneration;
                    failedExitChecks = 0;
                    tunnelMissing = 0;
                }
                if (proc is not null
                    && Os.PhysicalBindAddress(cfg.TunAddress)?.ToString() is { } bind
                    && boundAddress is not null && bind != boundAddress)
                {
                    Log.Info($"адрес сети сменился: {boundAddress} -> {bind}, перезапускаю туннель");
                    await QueueAutomaticRecovery("network-changed", Strings.T(cfg.Language, "reconnect_network_changed"), observedGeneration);
                }

                tunnelMissing = proc is not null && proc.IsRunning && !NodeProbe.TunnelIsUp(cfg.TunAddress)
                    ? tunnelMissing + 1 : 0;
                if (tunnelMissing >= 2)
                {
                    Log.Warn("движок работает, но туннеля в системе нет — перезапускаю туннель");
                    tunnelMissing = 0;
                    await QueueAutomaticRecovery("tunnel-missing", Strings.T(cfg.Language, "reconnect_tunnel_missing"), observedGeneration);
                }

                if (Os.IsWindows && Os.IsElevated())
                {
                    // Snapshot selection and reconciliation share the apply/start queue,
                    // so an older observer cannot reinstall superseded guard rules.
                    using var gate = EngineMutex.Acquire(Ceho.Root);
                    LeakGuard.Apply(admittedConfiguration.Snapshot(), Ceho.Root);
                }

                if (proc is null && guard is not null && !NodeProbe.TunnelIsUp(CehoConfig.GuardTunAddress))
                {
                    using (EngineMutex.Acquire(Ceho.Root))
                    {
                        if (proc is null && guard is not null && !NodeProbe.TunnelIsUp(CehoConfig.GuardTunAddress))
                        {
                            StopGuard();
                            ScheduleGuardRecovery(Strings.T(cfg.Language, "reconnect_tunnel_missing"));
                            await StartGuard();
                        }
                    }
                }

                if (proc is not null)
                {
                    var current = activeEngineConfig ?? admittedConfiguration.Snapshot();
                    var port = current.MixedPort;
                    var observedExit = await Ceho.ProbeExitAsync(port);
                    using (EngineMutex.Acquire(Ceho.Root))
                    {
                        if (!recovery.IsCurrent(observedGeneration) || proc is null) continue;
                        (exitCountry, exitIp) = observedExit;
                        probed = true;
                    }

                    // Exit-IP metadata can be unavailable even when traffic works.
                    // Confirm loss of actual connectivity twice before recovery.
                    var online = observedExit.Item2 is not null
                        || await Ceho.CheckSubscriptionLiveAsync(port, current.CheckUrl);
                    using (EngineMutex.Acquire(Ceho.Root))
                    {
                        if (!recovery.IsCurrent(observedGeneration) || proc is null) continue;
                        if (online)
                        {
                            failedExitChecks = 0;
                            recovery.ObserveHealthy(Environment.TickCount64);
                        }
                        else
                        {
                            failedExitChecks++;
                            recovery.ObserveUnhealthy();
                        }
                    }
                    if (!online && failedExitChecks >= 2 && current.RotationEnabled)
                    {
                        var reason = current.Language == "ru"
                            ? "Две проверки подряд не подтвердили доступ через туннель."
                            : "Two consecutive checks could not confirm connectivity through the tunnel.";
                        // Refresh only inside the admitted engine attempt, using
                        // current settings. This observer never writes caches/rules.
                        await QueueAutomaticRecovery("exit-unavailable", reason, observedGeneration);
                    }
                }
            }
            catch (Exception ex) { Log.Error("проверка выхода не завершилась", ex); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), cts.Token); } catch { return; }
        }
    });

    _ = Task.Run(async () =>
    {
        var first = true;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(first ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(6), cts.Token);
            }
            catch { return; }
            first = false;

            var c = CehoConfig.Load(Ceho.ConfigPath);
            if (c.EngineAutoUpdate && Os.ResolveSingBox(Ceho.Root) is { } engine
                && Installer.EngineOutdated(Installer.EngineVersionOf(engine)))
            {
                try
                {
                    Log.Warn(Strings.T(c.Language, "engine_auto_starting", Installer.EngineVersion));
                    Log.Warn(await UpdateEngineAsync(new DelegateReport(Log.Info)));
                }
                catch (Exception ex)
                {
                    Log.Error(Strings.T(c.Language, "engine_auto_failed", ex.Message));
                }
            }
            if (!c.AutoUpdate) continue;

            try
            {
                var release = await Updater.CheckAsync(c.UpdateRepo);
                if (release is null) continue;
                if (UpdateRollback.WasRolledBack(Ceho.Root, release.Version))
                {
                    Log.Info(Strings.T(c.Language, "upd_auto_skip_rolled_back", release.Version));
                    continue;
                }

                Log.Warn(Strings.T(c.Language, "upd_auto_starting", release.Version));
                var outcome = await web.OnUpdate!(true, new DelegateReport(Log.Info));
                c = CehoConfig.Load(Ceho.ConfigPath);
                c.AutoUpdatedVersion = release.Version;
                c.AutoUpdatedAtUtc = DateTime.UtcNow;
                c.SaveSubscriptionStatus(Ceho.ConfigPath);
                Log.Warn(Strings.T(c.Language, "upd_auto_installed", release.Version) + " " + outcome);
            }
            catch (Exception ex)
            {
                Log.Error(Strings.T(c.Language, "upd_auto_failed", ex.Message));
            }
        }
    });

    DaemonControl.WaitForStop();
    cts.Cancel();
    shuttingDown = true;
    if (DaemonControl.KeepEngineRequested(Ceho.Root) && proc is { IsRunning: true })
        Log.Info("служба остановлена для обновления: движок оставлен работать");
    else
        StopTunnel();
    StopGuard();
    TunCleanup.KillOurProcesses(guardConfigPath, Log.Info);
    web.Stop();
    DaemonControl.ClearRunning(Ceho.Root);
    Console.Error.WriteLine(Strings.T(cfg.Language, "stopped"));
    return 0;
}

Console.Error.WriteLine(Strings.T(cfg0.Language, "err_unknown_command", cmd));
if (Cli.Suggest(cmd) is { } guess)
    Console.Error.WriteLine(Strings.T(cfg0.Language, "err_did_you_mean", guess));
return 1;

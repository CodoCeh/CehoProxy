namespace ProxyCage.Core;

/// <summary>Что доктор умеет починить сам. Всё остальное — только советом.</summary>
public enum Repair
{
    None,
    Engine,
    Rules,
    Leftovers,
    PanelPort,
    ProxyPort,
    Service,
}

/// <summary>
/// Руки доктора: подписки и правила живут в приложении, а не в ядре,
/// поэтому нужные действия он получает снаружи — одни и те же для панели и терминала.
/// </summary>
public sealed class DoctorTools
{
    public Func<IStageReport, Task<IReadOnlyList<ProxyNode>>>? Pool { get; init; }
    public Func<IStageReport, Task<string>>? Rebuild { get; init; }
    public Func<Task<(string? Country, string? Ip)>>? Exit { get; init; }
}

/// <summary>
/// Полный осмотр: установлено, настроено, работает. И починка того, что можно
/// починить без человека. Панель и CLI зовут ровно это, поэтому отвечают одинаково.
/// </summary>
public static class Doctor
{
    public sealed record Result(
        IReadOnlyList<Preflight.Check> Checks,
        IReadOnlyList<string> Done,
        IReadOnlyList<string> Left)
    {
        public int Blockers => Checks.Count(c => c.Level == Preflight.Level.Blocker);
        public int Warnings => Checks.Count(c => c.Level == Preflight.Level.Warning);
        public bool Healthy => Blockers == 0;
        public bool Fixable => Checks.Any(c => c.Repair != Repair.None);
    }

    public static async Task<Result> CheckAsync(
        CehoConfig cfg, string root, DoctorTools? tools = null, IStageReport? p = null)
    {
        var l = cfg.Language;
        string S(string key, params object[] a) => Strings.T(l, key, a);

        p?.Stage(S("doc_stage_install"), 5);
        var checks = Preflight.Run(cfg, root).ToList();

        var engine = Os.ResolveSingBox(root);
        if (engine is not null)
        {
            p?.Stage(S("doc_stage_engine"), 20);
            checks.Add(EngineRuns(engine, l));
        }

        p?.Stage(S("doc_stage_rules"), 35);
        checks.Add(Rules(cfg, root, engine, l));

        if (tools?.Pool is not null && cfg.Subscriptions.Count > 0)
        {
            p?.Stage(S("doc_stage_subs"), 50);
            checks.AddRange(await PoolChecksAsync(cfg, tools.Pool, p, l));
        }

        p?.Stage(S("doc_stage_traces"), 75);
        checks.AddRange(Traces(cfg, root, l));
        checks.AddRange(Neighbours(cfg, l));
        checks.AddRange(UnmanagedAiTools(cfg, l));

        var running = DaemonControl.IsRunning(root) && NodeProbe.TunnelIsUp(cfg.TunAddress);
        if (running && tools?.Exit is not null)
        {
            p?.Stage(S("doc_stage_exit"), 85);
            var (country, ip) = await tools.Exit();
            checks.Add(ip is null
                ? new Preflight.Check(Preflight.Level.Blocker,
                    S("doc_exit_dead"), S("doc_exit_dead_detail"), S("doc_exit_dead_fix"))
                : new Preflight.Check(Preflight.Level.Ok,
                    S("exit_is", country ?? "?", ip), null, null));
        }

        p?.Stage(S("doc_stage_log"), 95);
        if (FreshCrash(l) is { } crash) checks.Add(crash);

        return new Result(checks, Array.Empty<string>(), Array.Empty<string>());
    }

    /// <summary>
    /// Осматривает, чинит что может и осматривает заново: в ответе и список сделанного,
    /// и то, что осталось человеку.
    /// </summary>
    public static async Task<Result> HealAsync(
        CehoConfig cfg, string configPath, string root, DoctorTools? tools = null, IStageReport? p = null)
    {
        var l = cfg.Language;
        string S(string key, params object[] a) => Strings.T(l, key, a);

        var before = await CheckAsync(cfg, root, tools, p);
        var done = new List<string>();
        var left = new List<string>();

        // Порядок не случайный: сначала убираем следы прошлого запуска, потом ставим
        // движок, и только потом собираем правила — иначе сборка упрётся в то же самое.
        foreach (var repair in new[]
                 {
                     Repair.Leftovers, Repair.Engine, Repair.PanelPort, Repair.ProxyPort,
                     Repair.Rules, Repair.Service,
                 })
        {
            if (!before.Checks.Any(c => c.Repair == repair)) continue;

            p?.Stage(S("doc_fixing", S(NameOf(repair))), Percent(repair));
            try
            {
                var said = await ApplyAsync(repair, cfg, configPath, root, tools, p, l);
                if (said is not null) done.Add(said);
            }
            catch (Exception ex)
            {
                Log.Error($"доктор не смог починить {repair}", ex);
                left.Add(S("doc_fix_failed", S(NameOf(repair)), ex.Message));
            }
        }

        p?.Stage(S("doc_stage_recheck"), 96);
        var after = await CheckAsync(CehoConfig.Load(configPath), root, tools);

        foreach (var check in after.Checks.Where(c => c.Level == Preflight.Level.Blocker))
            if (check.Repair == Repair.None && check.Fix is { } advice)
                left.Add($"{check.Title} — {advice}");

        return after with { Done = done, Left = left };
    }

    /// <summary>
    /// Итог осмотра одной строкой. Замечания в него попадают обязательно: «всё в порядке»
    /// рядом с замечанием про мёртвый прокси человек просто пролистывает.
    /// </summary>
    public static string Headline(Result r, string l)
    {
        string S(string key, params object[] a) => Strings.T(l, key, a);

        if (!r.Healthy) return S("pf_blockers", r.Blockers);
        return r.Warnings > 0 ? $"{S("doc_all_ok")} · {S("doc_warnings", r.Warnings)}" : S("doc_all_ok");
    }

    /// <summary>Итог починки одной фразой: её показывают и панель, и терминал.</summary>
    public static string Say(Result r, string l)
    {
        string S(string key, params object[] a) => Strings.T(l, key, a);

        if (r.Healthy)
            return r.Done.Count == 0 ? Headline(r, l) : $"{string.Join(" ", r.Done)} {Headline(r, l)}";

        if (r.Done.Count == 0)
            return r.Left.Count == 0 ? S("doc_nothing_to_fix") : S("doc_left_n", r.Left.Count);

        return $"{string.Join(" ", r.Done)} {S("doc_left_n", r.Left.Count)}";
    }

    private static async Task<string?> ApplyAsync(
        Repair repair, CehoConfig cfg, string configPath, string root,
        DoctorTools? tools, IStageReport? p, string l)
    {
        string S(string key, params object[] a) => Strings.T(l, key, a);

        switch (repair)
        {
            case Repair.Leftovers:
            {
                var killed = TunCleanup.KillOurProcesses(Path.Combine(root, "singbox.json"), m => p?.Note(m));
                var gone = TunCleanup.RemoveLeftovers(m => p?.Note(m), cfg.TunAddress, root);
                DaemonControl.ClearRunning(root);
                return S("doc_did_leftovers", killed + gone);
            }

            case Repair.Engine:
            {
                if (!Preflight.FolderIsWritable(root, out var why))
                    throw new InvalidOperationException(why);

                var engine = await Installer.DownloadEngineAsync(root, m => p?.Note(m), l);
                return S("doc_did_engine", engine);
            }

            case Repair.PanelPort:
            case Repair.ProxyPort:
            {
                var fresh = CehoConfig.Load(configPath);
                var old = repair == Repair.PanelPort ? fresh.WebPort : fresh.MixedPort;
                var free = Preflight.NextFreePort(old);
                if (repair == Repair.PanelPort) fresh.WebPort = free; else fresh.MixedPort = free;
                fresh.Save(configPath);
                if (repair == Repair.PanelPort) cfg.WebPort = free; else cfg.MixedPort = free;
                return S(repair == Repair.PanelPort ? "doc_did_panel_port" : "doc_did_proxy_port", old, free);
            }

            case Repair.Rules:
            {
                if (tools?.Rebuild is null) return null;
                var said = await tools.Rebuild(p ?? new DelegateReport(_ => { }));
                return said;
            }

            case Repair.Service:
                Autostart.Restart();
                await Task.Delay(TimeSpan.FromSeconds(2));
                return S("doc_did_service");

            default:
                return null;
        }
    }

    private static string NameOf(Repair repair) => repair switch
    {
        Repair.Engine => "doc_name_engine",
        Repair.Rules => "doc_name_rules",
        Repair.Leftovers => "doc_name_leftovers",
        Repair.PanelPort => "doc_name_panel_port",
        Repair.ProxyPort => "doc_name_proxy_port",
        Repair.Service => "doc_name_service",
        _ => "doc_name_none",
    };

    private static int Percent(Repair repair) => repair switch
    {
        Repair.Leftovers => 20,
        Repair.Engine => 35,
        Repair.PanelPort => 55,
        Repair.ProxyPort => 60,
        Repair.Rules => 70,
        _ => 90,
    };

    private static Preflight.Check EngineRuns(string engine, string l)
    {
        string S(string key, params object[] a) => Strings.T(l, key, a);

        var (code, output) = Os.Run(engine, "version", 10000);
        var line = output.Split('\n').FirstOrDefault(s => s.Trim().Length > 0)?.Trim() ?? "";

        if (code == 0 && line.Length > 0)
            return new Preflight.Check(Preflight.Level.Ok, S("doc_engine_runs", line), null, null);

        return new Preflight.Check(Preflight.Level.Blocker,
            S("doc_engine_broken"),
            line.Length > 0 ? line : S("doc_engine_broken_detail", code),
            S("doc_engine_broken_fix"),
            Repair.Engine);
    }

    private static Preflight.Check Rules(CehoConfig cfg, string root, string? engine, string l)
    {
        string S(string key, params object[] a) => Strings.T(l, key, a);

        var rules = Path.Combine(root, "singbox.json");
        if (!File.Exists(rules))
            return new Preflight.Check(Preflight.Level.Blocker,
                S("doc_rules_missing"), S("doc_rules_missing_detail"), S("doc_rules_fix"), Repair.Rules);

        var config = Path.Combine(root, "config.json");
        if (File.Exists(config) && File.GetLastWriteTimeUtc(config) > File.GetLastWriteTimeUtc(rules))
            return new Preflight.Check(Preflight.Level.Warning,
                S("doc_rules_stale"), S("doc_rules_stale_detail"), S("doc_rules_fix"), Repair.Rules);

        if (engine is not null)
        {
            var (code, output) = Os.Run(engine, $"check -c \"{rules}\"", 15000);
            if (code != 0)
                return new Preflight.Check(Preflight.Level.Blocker,
                    S("doc_rules_bad"), Short(output), S("doc_rules_fix"), Repair.Rules);
        }

        return new Preflight.Check(Preflight.Level.Ok, S("doc_rules_ok"), rules, null);
    }

    private static async Task<IReadOnlyList<Preflight.Check>> PoolChecksAsync(
        CehoConfig cfg, Func<IStageReport, Task<IReadOnlyList<ProxyNode>>> pool, IStageReport? p, string l)
    {
        string S(string key, params object[] a) => Strings.T(l, key, a);
        var checks = new List<Preflight.Check>();

        IReadOnlyList<ProxyNode> nodes;
        try
        {
            nodes = await pool(p ?? new DelegateReport(_ => { }));
        }
        catch (Exception ex)
        {
            checks.Add(new Preflight.Check(Preflight.Level.Blocker,
                S("doc_subs_dead"), ex.Message, S("doc_subs_dead_fix")));
            return checks;
        }

        if (nodes.Count == 0)
        {
            checks.Add(new Preflight.Check(Preflight.Level.Blocker,
                S("doc_subs_empty"), S("doc_subs_empty_detail"), S("doc_subs_dead_fix")));
            return checks;
        }

        checks.Add(new Preflight.Check(Preflight.Level.Ok, S("doc_subs_ok", nodes.Count), null, null));

        try
        {
            var live = SingBoxConfigGenerator.BuildPool(nodes, cfg);
            checks.Add(new Preflight.Check(Preflight.Level.Ok, S("doc_pool_ok", live.Count), null, null));
        }
        catch (PoolEmptyException ex)
        {
            checks.Add(new Preflight.Check(Preflight.Level.Blocker,
                S("doc_pool_empty"), ex.Message, S("doc_pool_empty_fix")));
        }

        return checks;
    }

    private static IEnumerable<Preflight.Check> Traces(CehoConfig cfg, string root, string l)
    {
        string S(string key, params object[] a) => Strings.T(l, key, a);

        var daemon = DaemonControl.IsRunning(root);
        var tunnel = NodeProbe.TunnelIsUp(cfg.TunAddress);

        if (tunnel && !daemon)
            yield return new Preflight.Check(Preflight.Level.Blocker,
                S("doc_leftovers"), S("doc_leftovers_detail"), S("doc_leftovers_fix"), Repair.Leftovers);
        else if (!daemon && File.Exists(DaemonControl.PidPath(root)))
            yield return new Preflight.Check(Preflight.Level.Warning,
                S("doc_stale_pid"), DaemonControl.PidPath(root), S("doc_leftovers_fix"), Repair.Leftovers);

        if (Autostart.IsEnabled())
        {
            if (daemon)
                yield return new Preflight.Check(Preflight.Level.Ok, S("doc_service_ok"), null, null);
            else
                yield return new Preflight.Check(Preflight.Level.Warning,
                    S("doc_service_dead"), S("doc_service_dead_detail"), S("doc_service_fix"), Repair.Service);
        }
        else
        {
            yield return new Preflight.Check(Preflight.Level.Warning,
                S("doc_service_off"), S("doc_service_off_detail"),
                S("doc_service_off_fix", Os.IsWindows ? "" : "sudo "));
        }
    }

    /// <summary>
    /// Что на машине делают соседи. Сюда попал живой случай: другой VPN-клиент оставил
    /// в системе прокси на мёртвом порту, браузер молчал — и виноватым выглядели мы.
    /// </summary>
    private static IEnumerable<Preflight.Check> Neighbours(CehoConfig cfg, string l)
    {
        if (!Os.IsWindows) yield break;

        string S(string key, params object[] a) => Strings.T(l, key, a);

        if (SystemProxy.DeadLoopbackProxy(cfg.MixedPort) is { } dead)
            yield return new Preflight.Check(Preflight.Level.Warning,
                S("doc_proxy_dead", dead), S("doc_proxy_dead_detail"), S("doc_proxy_dead_fix"));

        var ours = TunCleanup.InterfaceWithAddress(cfg.TunAddress);
        foreach (var alien in SystemProxy.OtherTunnels(ours))
            yield return new Preflight.Check(Preflight.Level.Warning,
                S("doc_alien_tun", alien), S("doc_alien_tun_detail"), null);
    }

    private static IEnumerable<Preflight.Check> UnmanagedAiTools(CehoConfig cfg, string l)
    {
        IReadOnlyList<AiTools.Found> found;
        try { found = AiTools.Detect(); }
        catch { yield break; }

        string S(string key, params object[] a) => Strings.T(l, key, a);

        var addedFolders = cfg.Apps
            .Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder))
            .Select(a => a.Folder.TrimEnd('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in found)
        {
            var folder = tool.Path.TrimEnd('\\', '/');
            if (addedFolders.Contains(folder)) continue;
            if (cfg.Apps.Any(a => a.Enabled && (a.Folder.Contains(tool.Name, StringComparison.OrdinalIgnoreCase)
                                                || a.Name.Equals(tool.Name, StringComparison.OrdinalIgnoreCase))))
                continue;

            yield return new Preflight.Check(
                Preflight.Level.Warning,
                S("doc_tool_unadded", tool.Name),
                S("doc_tool_unadded_detail", tool.Name, tool.Path),
                S("doc_tool_unadded_fix", tool.Name));
        }
    }

    private static Preflight.Check? FreshCrash(string l)
    {
        var crash = Log.Crashes(1).FirstOrDefault();
        if (crash is null || DateTime.Now - crash.When > TimeSpan.FromHours(24)) return null;

        return new Preflight.Check(Preflight.Level.Warning,
            Strings.T(l, "doc_crash", crash.When.ToString("dd.MM HH:mm")),
            crash.Context,
            Strings.T(l, "doc_crash_fix"));
    }

    private static string Short(string text)
    {
        var line = text.Split('\n').FirstOrDefault(s => s.Trim().Length > 0)?.Trim() ?? "";
        return line.Length > 300 ? line[..300] + "…" : line;
    }
}

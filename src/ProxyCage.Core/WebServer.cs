using System.Net;
using System.Text;
using System.Web;

namespace ProxyCage.Core;

public sealed class WebServer
{
    private const string CookieName = "ceho";

    private const string JobPool = "pool";
    private const string JobMeasure = "measure";
    private const string JobApply = "apply";
    private const string JobPower = "power";
    private const string JobUpdate = "update";
    private const string JobSubs = "subs";
    private const string JobEngine = "engine";
    private const string JobDoctor = "doctor";

    private readonly string _configPath;
    private readonly Func<ControlState> _state;
    private readonly Action<string> _log;
    private HttpListener? _listener;

    public sealed record ControlState(
        bool Running, string? ExitCountry, string? ExitIp, string? LastError, bool Probed);

    public Func<IStageReport, Task<IReadOnlyList<NodeProbe.CountryRow>>>? OnCountries { get; set; }
    private IReadOnlyList<NodeProbe.CountryRow>? _countries;

    public Func<IStageReport, Task<IReadOnlyList<ProxyNode>>>? OnPool { get; set; }

    // Пул держим здесь: страницу нельзя заставлять ждать скачивания подписок.
    private IReadOnlyList<ProxyNode>? _pool;
    private DateTime _poolAtUtc;
    private string? _poolError;

    public Func<IStageReport, Task<string?>>? OnStart { get; set; }
    public Func<Task<string?>>? OnStop { get; set; }
    public Func<IStageReport, Task<string?>>? OnRestart { get; set; }
    public Func<IStageReport, Task<string>>? OnApply { get; set; }

    public Func<bool, IStageReport, Task<string>>? OnUpdate { get; set; }

    public Func<IStageReport, Task<string>>? OnCheckSubs { get; set; }
    public Func<Task<string>>? OnUninstall { get; set; }

    /// <summary>Спросить у выхода страну и адрес: нужно доктору, чтобы убедиться, что защита работает.</summary>
    public Func<Task<(string? Country, string? Ip)>>? OnExit { get; set; }

    // Последний осмотр держим здесь: страница показывает его сразу, без ожидания проверок.
    private Doctor.Result? _doctor;
    private DateTime _doctorAtUtc;

    public Func<IReadOnlyList<string>>? WrappedNames { get; set; }

    public WebServer(string configPath, Func<ControlState> state, Action<string> log)
    {
        _configPath = configPath;
        _state = state;
        _log = log;
    }

    private string Root => Path.GetDirectoryName(_configPath) ?? ".";

    public void Start(int port)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        Auth.WritePanelPointer(Root, port, CehoConfig.Load(_configPath).MixedPort);
        _log(Strings.T(CehoConfig.Load(_configPath).Language, "panel_at", $"http://127.0.0.1:{port}"));
        _ = Task.Run(LoopAsync);
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { }
    }

    private async Task LoopAsync()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }

            // Каждый запрос — сам по себе. Раньше страница ждала, пока закончится
            // предыдущее долгое действие, и панель выглядела зависшей.
            _ = ServeAsync(ctx);
        }
    }

    private async Task ServeAsync(HttpListenerContext ctx)
    {
        try { await HandleAsync(ctx); }
        catch (Exception ex)
        {
            Log.Error($"панель не смогла ответить на {ctx.Request.Url?.AbsolutePath}", ex);
            _log($"ошибка панели: {ex.Message}");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var cfg = CehoConfig.Load(_configPath);

        if (path == "/api")
        {
            await HandleApiAsync(ctx, cfg);
            return;
        }

        if (!Authorized(ctx, cfg))
        {
            await HandleGateAsync(ctx, cfg, path);
            return;
        }

        if (path == "/job")
        {
            await HandleJobAsync(ctx);
            return;
        }

        if (path.StartsWith("/log/", StringComparison.Ordinal))
        {
            await HandleLogFileAsync(ctx, path);
            return;
        }

        if (ctx.Request.HttpMethod == "POST")
        {
            var form = await ReadFormAsync(ctx.Request);
            var (msg, isError, jobId) = await ApplyPostAsync(path, form, cfg);
            var tab = form.GetValueOrDefault("tab", "state");
            var q = $"?tab={Uri.EscapeDataString(tab)}";
            if (msg is not null) q += $"&m={Uri.EscapeDataString(msg)}&e={(isError ? 1 : 0)}";
            if (jobId is not null) q += $"&job={Uri.EscapeDataString(jobId)}";
            Redirect(ctx, "/" + q);
            return;
        }

        var flash = ctx.Request.QueryString["m"];
        var flashErr = ctx.Request.QueryString["e"] == "1";
        var current = ctx.Request.QueryString["tab"] ?? "state";
        var job = Jobs.Find(ctx.Request.QueryString["job"]);
        var view = ViewFromQuery(ctx.Request.QueryString["view"]);
        await WriteHtmlAsync(ctx, RenderPage(cfg, _state(), current, flash, flashErr, job, view));
    }

    private static bool Authorized(HttpListenerContext ctx, CehoConfig cfg)
    {
        if (!Auth.HasPassword(cfg)) return true;
        var cookie = ctx.Request.Cookies[CookieName]?.Value;
        return Auth.ValidSession(cookie);
    }

    private async Task HandleGateAsync(HttpListenerContext ctx, CehoConfig cfg, string path)
    {
        string? error = null;

        if (ctx.Request.HttpMethod == "POST" && path == "/login")
        {
            var form = await ReadFormAsync(ctx.Request);
            if (Auth.Verify(cfg, form.GetValueOrDefault("password", "")))
            {
                var token = Auth.IssueSession();

                ctx.Response.Headers.Add("Set-Cookie",
                    $"{CookieName}={token}; Path=/; HttpOnly; SameSite=Strict");
                Redirect(ctx, "/");
                return;
            }
            error = Strings.T(cfg.Language, "auth_wrong");
        }

        await WriteHtmlAsync(ctx, RenderGate(cfg, error));
    }

    public Func<string[], Task<(bool Ok, string Text)>>? OnApiCommand { get; set; }

    private async Task HandleApiAsync(HttpListenerContext ctx, CehoConfig cfg)
    {
        if (ctx.Request.HttpMethod != "POST") { ctx.Response.StatusCode = 405; ctx.Response.Close(); return; }

        var password = ctx.Request.Headers["X-Ceho-Password"];
        if (!Auth.Verify(cfg, password))
        {
            ctx.Response.StatusCode = 401;
            await WriteJsonAsync(ctx, false, Strings.T(cfg.Language, "auth_wrong"));
            return;
        }

        string body;
        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
            body = await reader.ReadToEndAsync();

        var argv = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.TrimEnd('\r')).ToArray();

        if (argv.Length == 0 || OnApiCommand is null)
        {
            await WriteJsonAsync(ctx, false, "empty command");
            return;
        }

        var (ok, text) = await OnApiCommand(argv);
        await WriteJsonAsync(ctx, ok, text);
    }

    private async Task HandleJobAsync(HttpListenerContext ctx)
    {
        var job = Jobs.Find(ctx.Request.QueryString["id"]);
        var payload = job is null
            ? new { state = "gone", percent = 100, stage = "", result = (string?)null, isError = false, seconds = 0.0 }
            : new
            {
                state = job.State switch
                {
                    JobState.Running => "running",
                    JobState.Done => "done",
                    _ => "failed",
                },
                percent = job.Percent,
                stage = job.Stage,
                result = job.Result,
                isError = job.IsError,
                seconds = Math.Round(job.Elapsed.TotalSeconds, 1),
            };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    /// <summary>
    /// Журнал текстом. Файл на диске один, поэтому «скачать» — это его нужная часть,
    /// а не отдельный файл под каждый вид записей.
    /// </summary>
    private async Task HandleLogFileAsync(HttpListenerContext ctx, string path)
    {
        if (path != "/log/download")
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        var view = ViewFromQuery(ctx.Request.QueryString["view"]);
        var text = string.Join(Environment.NewLine, Log.Tail(5000, view));
        var bytes = Encoding.UTF8.GetBytes(text.Length == 0 ? "журнал пуст" : text);

        var name = view switch
        {
            LogView.Engine => "cehoproxy-движок.log",
            LogView.Crashes => "cehoproxy-падения.log",
            _ => "cehoproxy.log",
        };

        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{name}\"");
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static LogView ViewFromQuery(string? value) => value switch
    {
        "engine" => LogView.Engine,
        "ours" => LogView.Ours,
        "crashes" => LogView.Crashes,
        _ => LogView.All,
    };

    private static async Task WriteJsonAsync(HttpListenerContext ctx, bool ok, string text)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { ok, text });
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static void Redirect(HttpListenerContext ctx, string location)
    {
        ctx.Response.StatusCode = 303;
        ctx.Response.RedirectLocation = location;
        ctx.Response.Close();
    }

    private static async Task WriteHtmlAsync(HttpListenerContext ctx, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private Job ApplyJob(CehoConfig cfg, string? doneMessage = null) =>
        Jobs.Start(JobApply, Strings.T(cfg.Language, "job_apply"), async p =>
        {
            var applied = OnApply is null ? Strings.T(cfg.Language, "rules_rebuilt") : await OnApply(p);
            return doneMessage is null ? applied : $"{doneMessage} {applied}";
        });

    private async Task<(string? Message, bool IsError, string? JobId)> ApplyPostAsync(
        string path, Dictionary<string, string> f, CehoConfig cfg)
    {
        string S(string key, params object[] a) => Strings.T(cfg.Language, key, a);

        try
        {
            switch (path)
            {
                case "/apps/add":
                {
                    var raw = f.GetValueOrDefault("path", "").Trim();
                    if (raw.Length == 0) return (S("err_need_path"), true, null);
                    if (!File.Exists(raw) && !Directory.Exists(raw))
                        return (S("err_no_such_path", raw), true, null);

                    var d = AppDetector.Detect(raw, cfg.Language);
                    if (cfg.Apps.Any(a => a.Folder.Equals(d.Folder, StringComparison.OrdinalIgnoreCase)))
                        return (S("err_already_added"), true, null);

                    cfg.Apps.Add(new AppEntry
                    {
                        Name = d.Name, Folder = d.Folder,
                        VersionAgnostic = d.VersionAgnostic,
                        SingleFile = d.SingleFile,
                        Launch = File.Exists(raw) ? raw : null,
                    });
                    Save(cfg);
                    return ($"{S("added_name", d.Name)}. {d.Explanation}", false, ApplyJob(cfg).Id);
                }

                case "/apps/detected":
                {
                    var raw = f.GetValueOrDefault("path", "").Trim();
                    if (raw.Length == 0) return (S("err_need_path"), true, null);

                    var d = AppDetector.Detect(raw, cfg.Language);
                    if (cfg.Apps.Any(a => a.Folder.Equals(d.Folder, StringComparison.OrdinalIgnoreCase)))
                        return (S("err_already_added"), true, null);

                    var name = f.GetValueOrDefault("name", d.Name);
                    cfg.Apps.Add(new AppEntry
                    {
                        Name = name,
                        Folder = d.Folder,
                        VersionAgnostic = d.VersionAgnostic,
                        SingleFile = d.SingleFile,
                    });
                    Save(cfg);
                    return ($"{S("added_name", name)}. {d.Explanation}", false, ApplyJob(cfg).Id);
                }

                case "/apps/remove":
                {
                    var folder = f.GetValueOrDefault("folder", "");
                    cfg.Apps.RemoveAll(a => a.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
                    Save(cfg);
                    return (S("removed"), false, cfg.Apps.Count > 0 ? ApplyJob(cfg).Id : null);
                }

                case "/subs/add":
                {
                    var url = f.GetValueOrDefault("url", "").Trim();
                    var name = f.GetValueOrDefault("name", "").Trim();
                    if (url.Length == 0) return (S("pf_no_subs_fix"), true, null);
                    if (name.Length == 0) name = $"sub{cfg.Subscriptions.Count + 1}";
                    if (cfg.Subscriptions.Any(s => s.Name == name))
                        return (S("err_sub_exists", name), true, null);

                    cfg.Subscriptions.Add(new SubscriptionEntry { Name = name, Url = url });
                    cfg.ActiveSubscription ??= name;
                    Save(cfg);
                    _pool = null;
                    return (S("sub_added", name), false, ApplyJob(cfg).Id);
                }

                case "/subs/check":
                {
                    if (OnCheckSubs is null) return ("no control", true, null);
                    var job = Jobs.Start(JobSubs, S("job_check_subs"), async p =>
                    {
                        var text = await OnCheckSubs(p);
                        _pool = null;
                        return text;
                    });
                    return (null, false, job.Id);
                }

                case "/subs/toggle":
                {
                    var name = f.GetValueOrDefault("name", "");
                    var entry = cfg.Subscriptions.FirstOrDefault(s => s.Name == name);
                    if (entry is null) return (S("err_no_such_sub", name), true, null);

                    var wanted = !entry.Enabled;
                    if (!wanted && cfg.Subscriptions.Count(s => s.Enabled) <= 1)
                        return (S("subs_last_one"), true, null);

                    entry.Enabled = wanted;
                    Save(cfg);
                    _pool = null;
                    return (S(wanted ? "sub_turned_on" : "sub_turned_off", name), false, ApplyJob(cfg).Id);
                }

                case "/subs/remove":
                {
                    var name = f.GetValueOrDefault("name", "");
                    cfg.Subscriptions.RemoveAll(s => s.Name == name);
                    if (cfg.ActiveSubscription == name)
                        cfg.ActiveSubscription = cfg.Subscriptions.FirstOrDefault()?.Name;
                    Save(cfg);
                    _pool = null;
                    try
                    {
                        var cache = Path.Combine(Root, $"sub-{name}.txt");
                        if (File.Exists(cache)) File.Delete(cache);
                    }
                    catch { }
                    return (S("sub_removed", name), false, null);
                }

                case "/subs/timeout":
                {
                    if (int.TryParse(f.GetValueOrDefault("timeout", "").Trim(), out var tSec) && tSec is >= 1 and <= 300)
                    {
                        cfg.TimeoutSeconds = tSec;
                        Save(cfg);
                        return (S("timeout_set", tSec), false, null);
                    }
                    return (S("err_need_timeout"), true, null);
                }

                case "/countries/save":
                {
                    var all = (f.GetValueOrDefault("all", "") ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var on = f.Keys.Where(k => k.StartsWith("c_", StringComparison.Ordinal))
                        .Select(k => k[2..]).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var prevExcluded = new List<string>(cfg.ExcludedCountries);
                    var prevPreferred = new List<string>(cfg.PreferredCountries);

                    cfg.ExcludedCountries = all.Where(c => !on.Contains(c)).ToList();
                    cfg.PreferredCountries.RemoveAll(c => cfg.ExcludedCountries.Contains(c, StringComparer.OrdinalIgnoreCase));
                    Save(cfg);

                    var job = Jobs.Start(JobApply, S("job_apply"), async p =>
                    {
                        try
                        {
                            return OnApply is null ? S("rules_rebuilt") : await OnApply(p);
                        }
                        catch (PoolEmptyException ex)
                        {
                            var back = CehoConfig.Load(_configPath);
                            back.ExcludedCountries = prevExcluded;
                            back.PreferredCountries = prevPreferred;
                            Save(back);
                            throw new InvalidOperationException($"{ex.Message} {S("change_reverted")}");
                        }
                    });
                    return (null, false, job.Id);
                }

                case "/nodes/save":
                {
                    // Ключи нод, показанных на странице: только про них и решаем.
                    // Ноды выключенных подписок в форму не попали — их выбор остаётся как был.
                    var shown = (f.GetValueOrDefault("all", "") ?? "")
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim())
                        .Where(k => k.Length > 0)
                        .ToList();
                    var keep = f.Keys.Where(k => k.StartsWith("n_", StringComparison.Ordinal))
                        .Select(k => k[2..]).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var prevBlocked = new List<string>(cfg.BlockedNodes);

                    var offScreen = cfg.BlockedNodes
                        .Where(k => !shown.Contains(k, StringComparer.OrdinalIgnoreCase));
                    var justBlocked = shown.Where(k => !keep.Contains(k));
                    cfg.BlockedNodes = offScreen.Concat(justBlocked)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    if (cfg.BlockedNodes.Count == prevBlocked.Count
                        && cfg.BlockedNodes.All(k => prevBlocked.Contains(k, StringComparer.OrdinalIgnoreCase)))
                        return (S("nodes_unchanged"), false, null);

                    Save(cfg);

                    var job = Jobs.Start(JobApply, S("job_apply"), async p =>
                    {
                        try
                        {
                            var text = OnApply is null ? S("rules_rebuilt") : await OnApply(p);
                            var off = CehoConfig.Load(_configPath).BlockedNodes.Count;
                            return off > 0 ? $"{text} · {S("nodes_off_now", off)}" : text;
                        }
                        catch (PoolEmptyException ex)
                        {
                            var back = CehoConfig.Load(_configPath);
                            back.BlockedNodes = prevBlocked;
                            Save(back);
                            throw new InvalidOperationException($"{ex.Message} {S("change_reverted")}");
                        }
                    });
                    return (null, false, job.Id);
                }

                case "/engine/download":
                {
                    if (Os.ResolveSingBox(Root) is { } here) return (S("engine_already", here), false, null);

                    var job = Jobs.Start(JobEngine, S("job_engine"), async p =>
                    {
                        p.Stage(S("job_engine"), 10);
                        var engine = await Task.Run(() => Installer.DownloadEngineAsync(
                            Root, m => p.Note(m), cfg.Language));
                        p.Stage(S("stage_saving"), 95);
                        return S("engine_ready", engine);
                    });
                    return (null, false, job.Id);
                }

                case "/doctor/check":
                {
                    var job = Jobs.Start(JobDoctor, S("job_doctor"), async p =>
                    {
                        var r = await Doctor.CheckAsync(CehoConfig.Load(_configPath), Root, Tools(), p);
                        Remember(r);
                        return Doctor.Headline(r, CehoConfig.Load(_configPath).Language);
                    });
                    return (null, false, job.Id);
                }

                case "/doctor/fix":
                {
                    var job = Jobs.Start(JobDoctor, S("job_heal"), async p =>
                    {
                        var r = await Doctor.HealAsync(CehoConfig.Load(_configPath), _configPath, Root, Tools(), p);
                        Remember(r);
                        return Doctor.Say(r, cfg.Language);
                    });
                    return (null, false, job.Id);
                }

                case "/countries/refresh":
                {
                    if (OnCountries is null) return (S("measure_blocked"), true, null);
                    if (NodeProbe.TunnelIsUp(cfg.TunAddress)) return (S("measure_blocked"), true, null);

                    var job = Jobs.Start(JobMeasure, S("job_measure"), async p =>
                    {
                        var rows = await OnCountries(p);

                        var items = rows.SelectMany(c => c.Items).ToList();
                        if (NodeProbe.LooksLikeLocalAccept(items))
                            throw new InvalidOperationException(S("speed_local_accept"));

                        p.Stage(S("stage_saving"), 95);
                        var fresh = CehoConfig.Load(_configPath);
                        foreach (var m in items)
                            if (m.LatencyMs is { } ms) fresh.NodeLatency[m.Node.Key] = ms;
                        Save(fresh);

                        _countries = rows;
                        return S("measure_done", rows.Count);
                    });
                    return (null, false, job.Id);
                }

                case "/pool/refresh":
                {
                    return (null, false, StartPoolJob(cfg).Id);
                }

                case "/settings":
                {
                    cfg.RotationEnabled = f.ContainsKey("rotation");
                    var checkUrl = f.GetValueOrDefault("checkurl", "").Trim();
                    if (checkUrl.Length > 0) cfg.CheckUrl = checkUrl;

                    if (int.TryParse(f.GetValueOrDefault("timeout", "").Trim(), out var tSec) && tSec is >= 1 and <= 300)
                        cfg.TimeoutSeconds = tSec;

                    var prevLimit = cfg.MaxLatencyMs;
                    var speed = f.GetValueOrDefault("speed", "").Trim();
                    cfg.MaxLatencyMs = int.TryParse(speed, out var limit) && limit > 0 ? limit : null;
                    Save(cfg);

                    var job = Jobs.Start(JobApply, S("job_apply"), async p =>
                    {
                        try
                        {
                            return OnApply is null ? S("rules_rebuilt") : await OnApply(p);
                        }
                        catch (PoolEmptyException ex)
                        {
                            var back = CehoConfig.Load(_configPath);
                            back.MaxLatencyMs = prevLimit;
                            Save(back);
                            throw new InvalidOperationException($"{ex.Message} {S("change_reverted")}");
                        }
                    });
                    return (null, false, job.Id);
                }

                case "/log/level":
                {
                    var level = f.GetValueOrDefault("level", "warn").Trim().ToLowerInvariant();
                    if (level is not ("debug" or "info" or "warn" or "error"))
                        return (S("log_level_bad"), true, null);
                    cfg.EngineLogLevel = level;
                    Save(cfg);
                    return (S("log_level_set", level), false, null);
                }

                case "/log/clear":
                {
                    Log.Clear();
                    return (S("log_cleared"), false, null);
                }

                case "/lang":
                {
                    cfg.Language = Strings.Normalize(f.GetValueOrDefault("lang", "ru"));
                    Save(cfg);
                    return (Strings.T(cfg.Language, "lang_set", cfg.Language), false, null);
                }

                case "/password":
                {
                    var pass = f.GetValueOrDefault("password", "");
                    var again = f.GetValueOrDefault("password2", "");
                    if (f.ContainsKey("clear"))
                    {
                        Auth.ClearPassword(cfg);
                        Save(cfg);
                        Auth.DropAllSessions();
                        return (S("auth_cleared"), false, null);
                    }
                    if (pass.Length < 4) return (S("setup_password_short"), true, null);
                    if (pass != again) return (S("setup_password_mismatch"), true, null);
                    Auth.SetPassword(cfg, pass);
                    Save(cfg);
                    Auth.DropAllSessions();
                    return (S("auth_set_ok"), false, null);
                }

                case "/update":
                {
                    if (OnUpdate is null) return ("no control", true, null);
                    var install = f.ContainsKey("install");
                    var job = Jobs.Start(JobUpdate, S(install ? "job_update" : "job_update_check"),
                        p => OnUpdate(install, p));
                    return (null, false, job.Id);
                }

                case "/autostart":
                {
                    var on = f.ContainsKey("enable");
                    var err = on
                        ? Autostart.Enable(
                            Environment.ProcessPath ?? (Os.IsWindows ? "cehoproxy.exe" : "/usr/local/bin/cehoproxy"),
                            Root)
                        : Autostart.Disable();
                    return err is null
                        ? (S(on ? "autostart_state_on" : "autostart_state_off"), false, null)
                        : (err, true, null);
                }

                case "/control/start":
                {
                    var job = Jobs.Start(JobPower, S("job_start"), async p =>
                    {
                        var err = OnStart is null ? "no control" : await OnStart(p);
                        if (err is not null) throw new InvalidOperationException(err);
                        return S("state_on");
                    });
                    return (null, false, job.Id);
                }

                case "/control/stop":
                {
                    var job = Jobs.Start(JobPower, S("job_stop"), async p =>
                    {
                        p.Stage(S("stage_stopping"), 40);
                        var err = OnStop is null ? "no control" : await OnStop();
                        if (err is not null) throw new InvalidOperationException(err);
                        return S("state_off");
                    });
                    return (null, false, job.Id);
                }

                case "/control/restart":
                {
                    var job = Jobs.Start(JobPower, S("job_restart"), async p =>
                    {
                        string? err;
                        if (OnRestart is not null) err = await OnRestart(p);
                        else if (OnStop is not null && OnStart is not null)
                        {
                            p.Stage(S("stage_stopping"), 20);
                            await OnStop();
                            err = await OnStart(p);
                        }
                        else err = "no control";

                        if (err is not null) throw new InvalidOperationException(err);
                        return S("rules_applied");
                    });
                    return (null, false, job.Id);
                }

                case "/uninstall":
                {
                    if (OnUninstall is not null)
                    {
                        var msg = await OnUninstall();
                        return (msg, false, null);
                    }
                    return ("no control", true, null);
                }
            }
            return (null, false, null);
        }
        catch (Exception ex)
        {
            Log.Error($"панель: действие {path}", ex);
            return (ex.Message, true, null);
        }
    }

    /// <summary>Руки доктора — те же действия, что у кнопок панели: подписки, сборка правил, опрос выхода.</summary>
    private DoctorTools Tools() => new()
    {
        Pool = OnPool,
        Rebuild = OnApply,
        Exit = OnExit,
    };

    private void Remember(Doctor.Result result)
    {
        _doctor = result;
        _doctorAtUtc = DateTime.UtcNow;
    }

    private Job StartPoolJob(CehoConfig cfg) =>
        Jobs.Start(JobPool, Strings.T(cfg.Language, "job_pool"), async p =>
        {
            try
            {
                var nodes = OnPool is null ? Array.Empty<ProxyNode>() : await OnPool(p);
                _pool = nodes;
                _poolAtUtc = DateTime.UtcNow;
                _poolError = null;
                return Strings.T(cfg.Language, "pool_loaded", nodes.Count);
            }
            catch (Exception ex)
            {
                _poolError = ex.Message;
                _poolAtUtc = DateTime.UtcNow;
                throw;
            }
        });

    private void Save(CehoConfig cfg)
    {
        cfg.Save(_configPath);
        Auth.RestrictConfigAccess(_configPath);
    }

    private static async Task<Dictionary<string, string>> ReadFormAsync(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            if (i < 0) { result[HttpUtility.UrlDecode(pair)] = ""; continue; }
            result[HttpUtility.UrlDecode(pair[..i])] = HttpUtility.UrlDecode(pair[(i + 1)..]);
        }
        return result;
    }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string RenderGate(CehoConfig cfg, string? error)
    {
        var sb = new StringBuilder();
        Head(sb, cfg, null);
        sb.Append("<div class=gate>");
        sb.Append("<img class=logo src=\"").Append(Brand.LogoDataUri).Append("\" alt=\"КодоЦех\">");
        sb.Append("<h1>CehoProxy</h1>");
        sb.Append("<p class=hint>").Append(E(Strings.T(cfg.Language, "auth_hint"))).Append("</p>");
        if (error is not null)
            sb.Append("<div class=\"flash err\">").Append(E(error)).Append("</div>");
        sb.Append("<form class=row method=post action=/login>");
        sb.Append("<input type=password name=password autofocus placeholder=\"")
          .Append(E(Strings.T(cfg.Language, "auth_password"))).Append("\">");
        sb.Append("<button>").Append(E(Strings.T(cfg.Language, "auth_enter"))).Append("</button></form>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void Head(StringBuilder sb, CehoConfig cfg, Job? job)
    {
        sb.Append("<!doctype html><html lang=").Append(cfg.Language).Append("><head><meta charset=utf-8>");
        sb.Append("<meta name=viewport content=\"width=device-width,initial-scale=1\">");

        // Без JavaScript страница с идущей операцией обновляется сама: панель управления
        // сетью обязана работать и в браузере с отключёнными скриптами.
        if (job is { Running: true })
            sb.Append("<noscript><meta http-equiv=refresh content=2></noscript>");

        sb.Append("<title>CehoProxy</title><style>").Append(WebUi.Css).Append("</style></head><body>");
    }

    private string RenderPage(
        CehoConfig cfg, ControlState st, string tab, string? flash, bool flashErr, Job? job,
        LogView logView = LogView.All)
    {
        string S(string key, params object[] a) => Strings.T(cfg.Language, key, a);
        var sb = new StringBuilder();
        Head(sb, cfg, job);
        sb.Append("<div class=wrap>");

        sb.Append("<header>");
        sb.Append("<img class=logo src=\"").Append(Brand.LogoDataUri).Append("\" alt=\"КодоЦех\">");
        sb.Append("<span class=mark>Ceho<span>Proxy</span></span>");
        sb.Append("<span class=where>").Append(E($"127.0.0.1:{cfg.WebPort}")).Append("</span></header>");

        var tabs = new (string Id, string Key)[]
        {
            ("state", "nav_state"), ("apps", "nav_apps"), ("subs", "nav_subs"),
            ("exit", "nav_exit"), ("browser", "nav_browser"), ("doctor", "nav_doctor"),
            ("log", "nav_log"), ("access", "nav_access"), ("help", "nav_help"),
        };
        sb.Append("<nav class=tabs>");
        foreach (var (id, key) in tabs)
            sb.Append("<a href=\"/?tab=").Append(id).Append('"')
              .Append(id == tab ? " class=on" : "").Append('>').Append(E(S(key))).Append("</a>");
        sb.Append("</nav>");

        if (!string.IsNullOrEmpty(flash))
            sb.Append("<div class=\"flash ").Append(flashErr ? "err" : "ok").Append("\">")
              .Append(E(flash)).Append("</div>");

        RenderJob(sb, cfg, job, tab, S);

        switch (tab)
        {
            case "apps": RenderApps(sb, cfg, S); break;
            case "subs": RenderSubs(sb, cfg, S); break;
            case "exit": RenderExit(sb, cfg, S); break;
            case "browser": RenderBrowser(sb, cfg, S); break;
            case "doctor": RenderDoctor(sb, cfg, S); break;
            case "log": RenderLog(sb, cfg, logView, S); break;
            case "access": RenderAccess(sb, cfg, S); break;
            case "help": RenderHelp(sb, cfg, S); break;
            default: RenderState(sb, cfg, st, S); break;
        }

        sb.Append("<footer><a class=forged href=\"").Append(Brand.Site).Append("\" target=_blank rel=noopener>")
          .Append("<img src=\"").Append(Brand.LogoDataUri).Append("\" alt=\"\">")
          .Append(E(S("forged"))).Append("</a>")
          .Append("<span class=foot-links><a href=\"").Append(Brand.Telegram)
          .Append("\" target=_blank rel=noopener>").Append(E(S("telegram"))).Append("</a>")
          .Append("<a href=\"").Append(E(Brand.RepoUrl(cfg.UpdateRepo)))
          .Append("\" target=_blank rel=noopener>").Append(E(S("product_page"))).Append("</a>")
          .Append("<span>").Append(E(S("footer_local"))).Append("</span></span></footer>");
        sb.Append("</div>");

        if (job is { Running: true }) sb.Append(WebUi.JobScript);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Полоса и этап: видно, что операция идёт и на чём именно она стоит.</summary>
    private static void RenderJob(
        StringBuilder sb, CehoConfig cfg, Job? job, string tab, Func<string, object[], string> S)
    {
        if (job is null) return;

        var cls = job.State switch
        {
            JobState.Running => "run",
            JobState.Done => "ok",
            _ => "err",
        };

        sb.Append("<div class=\"job ").Append(cls).Append("\" id=jp data-job=\"").Append(E(job.Id)).Append("\">");
        sb.Append("<div class=job-head><b>").Append(E(job.Title)).Append("</b>")
          .Append("<span class=job-num id=jn>").Append(job.Percent).Append("%</span></div>");
        sb.Append("<div class=bar><span id=jf style=\"width:").Append(job.Percent).Append("%\"></span></div>");
        sb.Append("<div class=job-stage id=js>").Append(E(job.Stage)).Append("</div>");
        sb.Append("<div class=job-foot>")
          .Append(E(S(job.Running ? "job_running" : job.IsError ? "job_failed" : "job_done",
              new object[] { job.Elapsed.TotalSeconds.ToString("F1") })));

        if (!job.Running)
            sb.Append(" · <a href=\"/?tab=").Append(E(tab)).Append("\">").Append(E(S("job_hide", [])))
              .Append("</a>");
        sb.Append("</div>");

        var steps = job.Steps;
        if (steps.Count > 1)
        {
            sb.Append("<details class=job-steps><summary>").Append(E(S("job_steps", [])))
              .Append("</summary><ol>");
            foreach (var step in steps) sb.Append("<li>").Append(E(step)).Append("</li>");
            sb.Append("</ol></details>");
        }

        sb.Append("</div>");
    }

    private void RenderState(StringBuilder sb, CehoConfig cfg, ControlState st, Func<string, object[], string> S)
    {
        sb.Append("<section>");
        var cls = !st.Running ? "off"
                : st.ExitIp is not null ? "on"
                : st.Probed ? "bad"
                : "wait";
        sb.Append("<div class=\"status ").Append(cls).Append("\"><span class=dot></span><b>");
        sb.Append(E(st.Running
            ? (cls == "bad" ? S("state_no_exit", []) : S("state_on", []))
            : S("state_off", []))).Append("</b>");
        sb.Append("<span class=detail>");
        if (st.Running && st.ExitIp is not null)
            sb.Append(E(S("exit_is", new object[] { st.ExitCountry ?? "?", st.ExitIp })));
        else if (st.Running && !st.Probed) sb.Append(E(S("state_checking", [])));
        else if (!st.Running) sb.Append(E(S("state_direct", [])));
        sb.Append("</span></div>");

        var checks = Preflight.Run(cfg, Root);
        var problems = checks.Where(c => c.Level != Preflight.Level.Ok)
            .Where(c => !c.Title.Contains("орт ", StringComparison.OrdinalIgnoreCase)
                     && !c.Title.Contains("ort ", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (st.LastError is not null && !problems.Any(c => st.LastError.Contains(c.Title, StringComparison.Ordinal)))
        {
            sb.Append("<div class=\"flash err\"><b>").Append(E(S("state_last_error", [])))
              .Append("</b>").Append(E(st.LastError))
              .Append("<br><a href=\"/?tab=log\">").Append(E(S("log_open", []))).Append("</a></div>");
        }

        var engineMissing = Os.ResolveSingBox(Root) is null;

        foreach (var c in problems)
        {
            sb.Append("<div class=\"flash").Append(c.Level == Preflight.Level.Blocker ? " err" : "").Append("\"><b>")
              .Append(E(c.Title)).Append("</b>");
            if (c.Detail is not null) sb.Append(E(c.Detail)).Append("<br>");
            if (c.Fix is not null) sb.Append(E(c.Fix));

            // В браузере набирать команду негде, поэтому движок скачивается кнопкой.
            if (engineMissing && c.Title.Contains(Os.SingBoxFileName, StringComparison.OrdinalIgnoreCase))
                sb.Append("<form class=row method=post action=/engine/download>")
                  .Append("<input type=hidden name=tab value=state><button>")
                  .Append(E(S("engine_get", []))).Append("</button></form>");
            sb.Append("</div>");
        }

        // Мешает что-то — не заставляем разбираться самому: доктор сначала осмотрит, потом починит.
        if (problems.Count > 0)
            sb.Append("<form class=row method=post action=\"/doctor/fix\">")
              .Append("<input type=hidden name=tab value=doctor>")
              .Append("<button class=ghost>").Append(E(S("doc_heal", []))).Append("</button></form>");

        if (st.Running)
        {
            sb.Append("<form class=row method=post>")
              .Append("<input type=hidden name=tab value=state>")
              .Append("<button type=submit formaction=\"/control/stop\" class=danger>")
              .Append(E(S("btn_off", [])))
              .Append("</button>")
              .Append("<button type=submit formaction=\"/control/restart\">")
              .Append(E(S("btn_restart", [])))
              .Append("</button></form>");
        }
        else
        {
            var canStart = Os.IsElevated();
            sb.Append("<form class=row method=post>")
              .Append("<input type=hidden name=tab value=state>")
              .Append("<button type=submit formaction=\"/control/start\"").Append(canStart ? "" : " disabled").Append('>')
              .Append(E(S("btn_on", [])))
              .Append("</button>")
              .Append("<button type=submit formaction=\"/control/restart\" class=ghost").Append(canStart ? "" : " disabled").Append('>')
              .Append(E(S("btn_restart", [])))
              .Append("</button></form>");
        }
        sb.Append("</section>");

        sb.Append("<section><h2>").Append(E(S("summary_title", []))).Append("</h2><dl class=kv>");
        sb.Append("<dt>").Append(E(S("nav_apps", []))).Append("</dt><dd>")
          .Append(cfg.Apps.Count(a => a.Enabled)).Append("</dd>");
        sb.Append("<dt>").Append(E(S("nav_subs", []))).Append("</dt><dd>")
          .Append(E(S("subs_on_of", new object[] { cfg.Subscriptions.Count(s => s.Enabled), cfg.Subscriptions.Count })))
          .Append("</dd>");
        sb.Append("<dt>").Append(E(S("nav_exit", []))).Append("</dt><dd>")
          .Append(E(cfg.PreferredCountries.Count > 0
              ? string.Join(", ", cfg.PreferredCountries)
              : cfg.ExcludedCountries.Count > 0
                  ? S("country_any_but", [string.Join(", ", cfg.ExcludedCountries)])
                  : S("country_any", [])));
        if (cfg.BlockedNodes.Count > 0)
            sb.Append(" · ").Append(E(S("nodes_off_now", new object[] { cfg.BlockedNodes.Count })));
        sb.Append("</dd>");
        sb.Append("<dt>").Append(E(S("nav_browser", []))).Append("</dt><dd>127.0.0.1:")
          .Append(cfg.MixedPort).Append("</dd>");

        var soonest = cfg.Subscriptions
            .Where(s => s.Enabled && s.ExpiresUtc is not null)
            .OrderBy(s => s.ExpiresUtc)
            .FirstOrDefault();
        if (soonest?.ExpiresUtc is { } when)
            sb.Append("<dt>").Append(E(S("subs_expiry_short", []))).Append("</dt><dd>")
              .Append(E(ExpiryText(when, S))).Append("</dd>");

        sb.Append("</dl></section>");

        sb.Append("<section><h2>").Append(E(S("upd_title", []))).Append("</h2>");
        sb.Append("<p class=hint>").Append(E(S("upd_current", new object[] { Updater.CurrentVersion })))
          .Append("</p>");
        sb.Append("<form class=row method=post action=/update><input type=hidden name=tab value=state>")
          .Append("<button class=ghost>").Append(E(S("upd_check", []))).Append("</button></form>");
        sb.Append("<form class=row method=post action=/update><input type=hidden name=tab value=state>")
          .Append("<input type=hidden name=install value=1>")
          .Append("<button class=ghost>").Append(E(S("upd_apply", []))).Append("</button></form></section>");

        sb.Append("<section><h2>").Append(E(S("autostart_title", []))).Append("</h2>");
        var auto = Autostart.IsEnabled();
        sb.Append("<div class=\"status ").Append(auto ? "on" : "off").Append("\"><span class=dot></span><b>")
          .Append(E(auto ? S("autostart_on", []) : S("autostart_off", []))).Append("</b></div>");
        sb.Append("<form class=row method=post action=/autostart><input type=hidden name=tab value=state>");
        if (!auto) sb.Append("<input type=hidden name=enable value=1>");
        sb.Append("<button class=ghost>").Append(E(auto ? S("autostart_del", []) : S("autostart_add", [])))
          .Append("</button></form></section>");
    }

    /// <summary>
    /// Осмотр в панели: одна кнопка проверяет всё разом, вторая чинит то, что чинится без человека.
    /// Итог держится в памяти, поэтому страница открывается сразу, а не ждёт проверок.
    /// </summary>
    private void RenderDoctor(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("doc_title", []))).Append("</h2>");
        sb.Append("<p class=hint>").Append(E(S("doc_hint", []))).Append("</p>");
        sb.Append("<form class=row method=post><input type=hidden name=tab value=doctor>")
          .Append("<button type=submit formaction=\"/doctor/check\">").Append(E(S("doc_check", [])))
          .Append("</button>")
          .Append("<button type=submit formaction=\"/doctor/fix\" class=ghost>").Append(E(S("doc_heal", [])))
          .Append("</button></form>");

        var report = _doctor;
        if (report is null)
        {
            sb.Append("<p class=hint>").Append(E(S("doc_never", []))).Append("</p></section>");
            return;
        }

        var head = report.Healthy
            ? (report.Warnings > 0 ? S("doc_warnings", [report.Warnings]) : S("doc_all_ok", []))
            : S("pf_blockers", [report.Blockers]);

        sb.Append("<div class=\"status ").Append(report.Healthy ? report.Warnings > 0 ? "wait" : "on" : "bad")
          .Append("\"><span class=dot></span><b>").Append(E(head)).Append("</b><span class=detail>")
          .Append(E(S("doc_when", [_doctorAtUtc.ToLocalTime().ToString("dd.MM HH:mm")])))
          .Append("</span></div>");

        if (report.Done.Count > 0)
        {
            sb.Append("<h2>").Append(E(S("doc_did_title", []))).Append("</h2><ul class=did>");
            foreach (var line in report.Done) sb.Append("<li>").Append(E(line)).Append("</li>");
            sb.Append("</ul>");
        }

        if (report.Left.Count > 0)
        {
            sb.Append("<h2>").Append(E(S("doc_left_title", []))).Append("</h2><ol class=steps>");
            foreach (var line in report.Left) sb.Append("<li>").Append(E(line)).Append("</li>");
            sb.Append("</ol>");
        }

        RenderChecks(sb, report.Checks, S);
        sb.Append("</section>");
    }

    private static void RenderChecks(
        StringBuilder sb, IReadOnlyList<Preflight.Check> checks, Func<string, object[], string> S)
    {
        sb.Append("<ul class=checks>");
        foreach (var c in checks.OrderBy(c => c.Level switch
                 {
                     Preflight.Level.Blocker => 0,
                     Preflight.Level.Warning => 1,
                     _ => 2,
                 }))
        {
            var (cls, mark) = c.Level switch
            {
                Preflight.Level.Ok => ("ok", "✓"),
                Preflight.Level.Warning => ("warn", "!"),
                _ => ("stop", "✕"),
            };

            sb.Append("<li class=").Append(cls).Append("><span class=mk>").Append(mark)
              .Append("</span><div><b>").Append(E(c.Title)).Append("</b>");
            if (c.Detail is not null) sb.Append("<span class=why>").Append(E(c.Detail)).Append("</span>");
            if (c.Fix is not null)
                sb.Append("<span class=\"why").Append(c.Repair != Repair.None ? " can" : "").Append("\">")
                  .Append(E(c.Repair != Repair.None ? c.Fix : S("doc_what_to_do", [c.Fix])))
                  .Append("</span>");
            sb.Append("</div></li>");
        }
        sb.Append("</ul>");
    }

    private void RenderApps(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("apps_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("apps_lede", []))).Append("</p>");

        if (cfg.Apps.Count == 0)
            sb.Append("<p class=empty>").Append(E(S("apps_empty", []))).Append("</p>");
        else
        {
            sb.Append("<table><tr><th>").Append(E(S("col_name", []))).Append("</th><th>")
              .Append(E(S("col_folder", []))).Append("</th><th></th></tr>");
            foreach (var a in cfg.Apps)
            {
                sb.Append("<tr><td>").Append(E(a.Name));
                if (a.VersionAgnostic) sb.Append("<br><span class=tag>Microsoft Store</span>");
                if (a.SingleFile) sb.Append("<br><span class=tag>").Append(E(S("col_file", []))).Append("</span>");
                sb.Append("</td><td class=path>").Append(E(a.Folder)).Append("</td><td class=actions>");
                sb.Append("<form method=post action=/apps/remove><input type=hidden name=tab value=apps>")
                  .Append("<input type=hidden name=folder value=\"").Append(E(a.Folder))
                  .Append("\"><button class=danger>").Append(E(S("btn_remove", []))).Append("</button></form>");
                sb.Append("</td></tr>");
            }
            sb.Append("</table>");
        }

        var placeholder = S(Os.Kind switch
        {
            OsKind.Windows => "apps_placeholder_win",
            OsKind.Mac => "apps_placeholder_mac",
            _ => "apps_placeholder_linux",
        }, []);
        sb.Append("<form class=row method=post action=/apps/add><input type=hidden name=tab value=apps>");
        sb.Append("<input type=text name=path placeholder=\"").Append(E(placeholder)).Append("\">");
        sb.Append("<button>").Append(E(S("btn_add", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("apps_hint", []))).Append(' ')
          .Append(E(Os.IsMac ? S("apps_hint_mac", []) : S("apps_hint_sysdir", []))).Append("</p>");

        RenderDetected(sb, cfg, S);
        sb.Append("</section>");
    }

    private void RenderDetected(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        IReadOnlyList<AiTools.Found> found;
        try { found = AiTools.Detect(); }
        catch { return; }

        sb.Append("</section><section><h2>").Append(E(S("ai_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("ai_lede", []))).Append("</p>");

        if (found.Count == 0)
            sb.Append("<p class=empty>").Append(E(S("ai_none", []))).Append("</p>");
        else
            RenderDetectedTable(sb, cfg, S, found);

        RenderTerminalAgents(sb, S, found);
    }

    private void RenderTerminalAgents(StringBuilder sb, Func<string, object[], string> S,
        IReadOnlyList<AiTools.Found> found)
    {
        sb.Append("<h2>").Append(E(S("run_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("run_lede", []))).Append("</p>");

        var sample = found.FirstOrDefault(t => t.Kind == AiTools.ToolKind.Script)?.Path;

        var name = sample is not null
            ? Path.GetFileNameWithoutExtension(sample)
            : S("run_sample_name", []);
        sb.Append("<dl class=kv>");
        sb.Append("<dt>").Append(E(S("run_once", []))).Append("</dt><dd>chp run ").Append(E(name)).Append("</dd>");
        sb.Append("<dt>").Append(E(S("run_always", []))).Append("</dt><dd>chp wrap ").Append(E(name)).Append("</dd>");
        sb.Append("<dt>").Append(E(S("run_undo", []))).Append("</dt><dd>chp unwrap ").Append(E(name)).Append("</dd>");
        sb.Append("</dl>");
        if (sample is null)
            sb.Append("<p class=hint>").Append(E(S("run_sample_hint", []))).Append("</p>");

        var wrapped = WrappedNames?.Invoke() ?? Array.Empty<string>();
        if (wrapped.Count > 0)
            sb.Append("<p class=hint>").Append(E(S("wrap_list", []))).Append(": ")
              .Append(E(string.Join(", ", wrapped))).Append("</p>");

        sb.Append("<p class=hint>").Append(E(S("run_note", []))).Append("</p>");
    }

    private static void RenderDetectedTable(StringBuilder sb, CehoConfig cfg,
        Func<string, object[], string> S, IReadOnlyList<AiTools.Found> found)
    {
        sb.Append("<table><tr><th>").Append(E(S("col_name", []))).Append("</th><th>")
          .Append(E(S("col_folder", []))).Append("</th><th></th></tr>");

        foreach (var tool in found)
        {
            var already = cfg.Apps.Any(a =>
                a.Folder.Equals(tool.Path, StringComparison.OrdinalIgnoreCase) ||
                tool.Path.StartsWith(a.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            sb.Append("<tr><td>").Append(E(tool.Name)).Append("<br><span class=tag>")
              .Append(E(S(tool.Kind switch
              {
                  AiTools.ToolKind.Bundle => "ai_kind_bundle",
                  AiTools.ToolKind.Script => "ai_kind_script",
                  _ => "ai_kind_native",
              }, []))).Append("</span></td>");

            sb.Append("<td class=path>").Append(E(tool.Path));
            if (tool.Kind == AiTools.ToolKind.Script && tool.Interpreter is not null)
                sb.Append("<br><span class=tag>")
                  .Append(E(S("ai_script_warn", new object[] { Path.GetFileName(tool.Interpreter) })))
                  .Append("</span>");
            sb.Append("</td><td class=actions>");

            if (already)
                sb.Append("<span class=tag>").Append(E(S("ai_added", []))).Append("</span>");
            else if (tool.Kind == AiTools.ToolKind.Script)
                sb.Append("<code>chp run ").Append(E(Path.GetFileNameWithoutExtension(tool.Path)))
                  .Append("</code>");
            else
                sb.Append("<form method=post action=/apps/detected><input type=hidden name=tab value=apps>")
                  .Append("<input type=hidden name=path value=\"").Append(E(tool.Path)).Append("\">")
                  .Append("<input type=hidden name=name value=\"").Append(E(tool.Name)).Append("\">")
                  .Append("<button>").Append(E(S("ai_add", []))).Append("</button></form>");

            sb.Append("</td></tr>");
        }
        sb.Append("</table>");
    }

    private static string ExpiryText(DateTime expiresUtc, Func<string, object[], string> S)
    {
        var days = SubscriptionInfo.DaysLeft(expiresUtc);
        var date = expiresUtc.ToLocalTime().ToString("dd.MM.yyyy");
        return days < 0
            ? S("sub_expired_on", new object[] { date })
            : S("sub_expires_on", new object[] { date, days });
    }

    private static void RenderSubs(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("nav_subs", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("subs_pool", []))).Append("</p>");

        if (cfg.Subscriptions.Count == 0)
            sb.Append("<p class=empty>").Append(E(S("subs_empty", []))).Append("</p>");
        else
        {
            sb.Append("<table><tr><th>").Append(E(S("col_use", []))).Append("</th><th>")
              .Append(E(S("col_name", []))).Append("</th><th>")
              .Append(E(S("col_until", []))).Append("</th><th>")
              .Append(E(S("col_traffic", []))).Append("</th><th>")
              .Append(E(S("col_state", []))).Append("</th><th></th></tr>");

            foreach (var s in cfg.Subscriptions)
            {
                sb.Append("<tr").Append(s.Enabled ? "" : " class=dim").Append('>');

                sb.Append("<td><form method=post action=/subs/toggle>")
                  .Append("<input type=hidden name=tab value=subs>")
                  .Append("<input type=hidden name=name value=\"").Append(E(s.Name)).Append("\">")
                  .Append("<button class=\"pill ").Append(s.Enabled ? "yes" : "no").Append("\">")
                  .Append(E(s.Enabled ? S("on_word", []) : S("off_word", [])))
                  .Append("</button></form></td>");

                sb.Append("<td>").Append(E(s.Name));
                if (s.LastNodes is { } nodes)
                    sb.Append("<br><span class=tag>").Append(E(S("sub_nodes_n", new object[] { nodes })))
                      .Append("</span>");
                sb.Append("<div class=path>").Append(E(s.Url)).Append("</div></td>");

                sb.Append("<td>");
                if (s.ExpiresUtc is { } when)
                {
                    var days = SubscriptionInfo.DaysLeft(when);
                    var cls = days < 0 ? "danger" : days <= 7 ? "warn" : "";
                    sb.Append("<span class=\"until ").Append(cls).Append("\">")
                      .Append(E(when.ToLocalTime().ToString("dd.MM.yyyy"))).Append("</span>");
                    sb.Append("<br><span class=tag>").Append(E(days < 0
                        ? S("sub_expired", [])
                        : S("sub_days_left", new object[] { days }))).Append("</span>");
                }
                else sb.Append("<span class=tag>").Append(E(S("sub_no_expiry", []))).Append("</span>");
                sb.Append("</td>");

                sb.Append("<td>");
                if (s.UsedBytes is { } used)
                {
                    sb.Append(E(SubscriptionInfo.Bytes(used)));
                    if (s.TotalBytes is { } total && total > 0)
                    {
                        var share = (int)Math.Clamp(used * 100 / total, 0, 100);
                        sb.Append(E(S("sub_of_total", new object[] { SubscriptionInfo.Bytes(total) })));
                        sb.Append("<div class=\"bar thin\"><span style=\"width:").Append(share)
                          .Append("%\"></span></div>");
                    }
                }
                else sb.Append("<span class=tag>").Append(E(S("sub_no_traffic", []))).Append("</span>");
                sb.Append("</td>");

                sb.Append("<td>").Append(E(s.LastCheckOk switch
                {
                    true => S("sub_ok", []),
                    false => S("sub_bad", []),
                    null => S("sub_unchecked", []),
                }));
                if (s.LastCheckedUtc is not null && DateTime.TryParse(s.LastCheckedUtc,
                        null, System.Globalization.DateTimeStyles.RoundtripKind, out var checked_))
                    sb.Append("<br><span class=tag>")
                      .Append(E(S("sub_checked_at", new object[] { checked_.ToLocalTime().ToString("dd.MM HH:mm") })))
                      .Append("</span>");
                if (s.LastError is not null)
                    sb.Append("<br><span class=\"tag bad\">").Append(E(s.LastError)).Append("</span>");
                sb.Append("</td>");

                sb.Append("<td class=actions>");
                sb.Append("<form method=post action=/subs/remove><input type=hidden name=tab value=subs>")
                  .Append("<input type=hidden name=name value=\"").Append(E(s.Name))
                  .Append("\"><button class=danger>").Append(E(S("btn_delete", []))).Append("</button></form>");
                sb.Append("</td></tr>");
            }
            sb.Append("</table>");
            sb.Append("<p class=hint>").Append(E(S("subs_toggle_hint", []))).Append("</p>");
            sb.Append("<p class=hint>").Append(E(S("subs_expiry_hint", []))).Append("</p>");
        }

        sb.Append("<form class=row method=post action=/subs/check><input type=hidden name=tab value=subs>")
          .Append("<button class=ghost>").Append(E(S("sub_check", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("sub_checking", []))).Append("</p>");

        sb.Append("<form class=row method=post action=/subs/add><input type=hidden name=tab value=subs>");
        sb.Append("<input type=text name=name placeholder=\"").Append(E(S("col_name", [])))
          .Append("\" style=\"flex:0 0 180px;min-width:130px\">");
        sb.Append("<input type=text name=url placeholder=\"https://…\">");
        sb.Append("<button>").Append(E(S("btn_add", []))).Append("</button></form>");

        sb.Append("<form class=row method=post action=/subs/timeout style=\"margin-top:16px\"><input type=hidden name=tab value=subs>");
        sb.Append("<span style=\"align-self:center\">").Append(E(S("timeout_label", []))).Append(":</span>");
        sb.Append("<input type=text name=timeout inputmode=numeric value=\"")
          .Append(cfg.TimeoutSeconds.ToString()).Append("\" style=\"flex:0 0 80px;min-width:60px\">");
        sb.Append("<button class=ghost>").Append(E(S("btn_save", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("timeout_hint", []))).Append("</p></section>");
    }

    private void RenderExit(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("countries_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("countries_hint", []))).Append("</p>");

        // Список стран рисуется по последнему известному пулу. Скачивать подписки прямо
        // в обработчике страницы нельзя: именно на этом панель и подвисала.
        var pool = _pool;
        var loading = Jobs.Active(JobPool);

        if (pool is null && loading is null && cfg.Subscriptions.Any(s => s.Enabled))
            loading = StartPoolJob(cfg);

        if (_poolError is not null)
            sb.Append("<div class=\"flash err\">").Append(E(_poolError)).Append("</div>");

        if (pool is null)
        {
            sb.Append("<p class=empty>").Append(E(S(loading is null
                ? "pf_no_subs_detail"
                : "pool_loading", []))).Append("</p>");
        }
        else
        {
            var groups = pool.GroupBy(n => n.CountryCode ?? CountryResolver.Unknown)
                .OrderByDescending(g => g.Count())
                .ToList();

            var measured = _countries?.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

            sb.Append("<form method=post action=/countries/save><input type=hidden name=tab value=exit>");
            sb.Append("<input type=hidden name=all value=\"")
              .Append(E(string.Join(",", groups.Select(g => g.Key)))).Append("\">");
            sb.Append("<table><tr><th>").Append(E(S("col_use", []))).Append("</th><th>")
              .Append(E(S("col_country", []))).Append("</th><th>").Append(E(S("col_nodes", [])))
              .Append("</th><th>").Append(E(S("col_alive", []))).Append("</th><th>")
              .Append(E(S("col_best", []))).Append("</th><th>").Append(E(S("col_protocols", [])))
              .Append("</th></tr>");

            foreach (var g in groups)
            {
                var on = !cfg.ExcludedCountries.Contains(g.Key, StringComparer.OrdinalIgnoreCase);
                var row = measured is not null && measured.TryGetValue(g.Key, out var m) ? m : null;
                var protocols = string.Join(", ", g.Select(n => n.Protocol.ToString()).Distinct());

                sb.Append("<tr><td><label class=check><input type=checkbox name=\"c_").Append(E(g.Key))
                  .Append('"').Append(on ? " checked" : "").Append("></label></td>");
                var countryName = g.Key == CountryResolver.Unknown
                    ? S("country_unknown", [])
                    : g.First().CountryName ?? g.Key;
                sb.Append("<td><span class=flag>").Append(CountryResolver.Flag(g.Key)).Append("</span> ")
                  .Append(E(countryName)).Append("</td>");
                sb.Append("<td class=num>").Append(g.Count()).Append("</td>");
                sb.Append("<td class=num>").Append(row is null ? "—" : row.Alive.ToString()).Append("</td>");
                sb.Append("<td class=num>")
                  .Append(row?.BestMs is null ? E(S("not_measured", [])) : row.BestMs + " ms").Append("</td>");
                sb.Append("<td class=tag>").Append(E(protocols)).Append("</td></tr>");
            }
            sb.Append("</table>");
            sb.Append("<button>").Append(E(S("btn_save", []))).Append("</button></form>");

            sb.Append("<p class=hint>").Append(E(S("pool_from", new object[]
            {
                pool.Count, _poolAtUtc.ToLocalTime().ToString("HH:mm:ss"),
            }))).Append("</p>");

            RenderNodes(sb, cfg, groups, S);
        }

        sb.Append("<form class=row method=post action=/pool/refresh><input type=hidden name=tab value=exit>")
          .Append("<button class=ghost>").Append(E(S("btn_pool_refresh", []))).Append("</button></form>");

        sb.Append("<form class=row method=post action=/countries/refresh><input type=hidden name=tab value=exit>")
          .Append("<button class=ghost>").Append(E(S("btn_measure", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("udp_not_measured", []))).Append("</p>");

        sb.Append("<h2>").Append(E(S("check_title", []))).Append("</h2>");
        sb.Append("<form class=stack method=post action=/settings><input type=hidden name=tab value=exit>");
        sb.Append("<label class=check><input type=checkbox name=rotation")
          .Append(cfg.RotationEnabled ? " checked" : "").Append("> ")
          .Append(E(S("rotation_label", []))).Append("</label>");
        sb.Append("<label class=field><span>").Append(E(S("checkurl_label", []))).Append("</span>")
          .Append("<input type=text name=checkurl value=\"").Append(E(cfg.CheckUrl)).Append("\"></label>");
        sb.Append("<p class=hint>").Append(E(S("checkurl_hint", []))).Append("</p>");
        sb.Append("<label class=field><span>").Append(E(S("speed_limit", []))).Append("</span>")
          .Append("<input type=text name=speed inputmode=numeric value=\"")
          .Append(cfg.MaxLatencyMs?.ToString() ?? "").Append("\" placeholder=\"500\"></label>");
        sb.Append("<p class=hint>").Append(E(S("speed_unmeasured_note", []))).Append("</p>");
        sb.Append("<label class=field><span>").Append(E(S("timeout_label", []))).Append("</span>")
          .Append("<input type=text name=timeout inputmode=numeric value=\"")
          .Append(cfg.TimeoutSeconds.ToString()).Append("\" placeholder=\"15\"></label>");
        sb.Append("<p class=hint>").Append(E(S("timeout_hint", []))).Append("</p>");
        sb.Append("<button class=ghost>").Append(E(S("btn_save", []))).Append("</button></form></section>");
    }

    /// <summary>
    /// Отдельные ноды: страна может быть разрешена целиком, а одну ноду из неё
    /// нужно убрать — например, она отвечает, но работает плохо.
    /// </summary>
    private static void RenderNodes(
        StringBuilder sb, CehoConfig cfg,
        List<IGrouping<string, ProxyNode>> groups, Func<string, object[], string> S)
    {
        var blocked = cfg.BlockedNodes.Count;

        sb.Append("<h2>").Append(E(S("nodes_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("nodes_hint", []))).Append("</p>");
        if (blocked > 0)
            sb.Append("<p class=hint>").Append(E(S("nodes_off_now", new object[] { blocked }))).Append("</p>");

        sb.Append("<form method=post action=/nodes/save><input type=hidden name=tab value=exit>");
        sb.Append("<input type=hidden name=all value=\"")
          .Append(E(string.Join("\n", groups.SelectMany(g => g).Select(n => n.Key)))).Append("\">");

        foreach (var g in groups)
        {
            var countryName = g.Key == CountryResolver.Unknown
                ? S("country_unknown", [])
                : g.First().CountryName ?? g.Key;
            var offHere = g.Count(n => SingBoxConfigGenerator.IsBlockedByHand(n, cfg));
            var countryOff = cfg.ExcludedCountries.Contains(g.Key, StringComparer.OrdinalIgnoreCase)
                || (cfg.PreferredCountries.Count > 0
                    && !cfg.PreferredCountries.Contains(g.Key, StringComparer.OrdinalIgnoreCase));

            // Страны с выключенными нодами раскрыты сразу: иначе выбор не найти.
            sb.Append("<details class=nodes").Append(offHere > 0 && !countryOff ? " open" : "")
              .Append("><summary>");
            sb.Append("<span class=flag>").Append(CountryResolver.Flag(g.Key)).Append("</span> ")
              .Append(E(countryName)).Append(" · ").Append(g.Count());
            if (offHere > 0)
                sb.Append(" · ").Append(E(S("nodes_off_here", new object[] { offHere })));
            // Иначе непонятно, почему галочка стоит, а нода всё равно не работает.
            if (countryOff)
                sb.Append(" · ").Append(E(S("nodes_country_off", [])));
            sb.Append("</summary><table>");
            sb.Append("<tr><th>").Append(E(S("col_use", []))).Append("</th><th>")
              .Append(E(S("col_node", []))).Append("</th><th>").Append(E(S("col_address", [])))
              .Append("</th><th>").Append(E(S("col_protocols", []))).Append("</th><th>")
              .Append(E(S("col_best", []))).Append("</th><th>").Append(E(S("col_source", [])))
              .Append("</th></tr>");

            foreach (var n in g.OrderBy(n => n.Remark, StringComparer.OrdinalIgnoreCase))
            {
                var on = !SingBoxConfigGenerator.IsBlockedByHand(n, cfg);
                var slow = SingBoxConfigGenerator.IsTooSlow(n, cfg);
                var ms = cfg.NodeLatency.TryGetValue(n.Key, out var value) ? value : (int?)null;

                sb.Append("<tr").Append(on ? "" : " class=off").Append("><td><label class=check>")
                  .Append("<input type=checkbox name=\"n_").Append(E(n.Key)).Append('"')
                  .Append(on ? " checked" : "").Append("></label></td>");
                sb.Append("<td>").Append(E(n.Remark.Length > 0 ? n.Remark : n.Tag)).Append("</td>");
                sb.Append("<td class=tag>").Append(E($"{n.Server}:{n.Port}")).Append("</td>");
                sb.Append("<td class=tag>").Append(E(n.Protocol.ToString())).Append("</td>");
                sb.Append("<td class=num>")
                  .Append(ms is null ? E(S("not_measured", [])) : ms + " ms")
                  .Append(slow ? " · " + E(S("node_slow", [])) : "").Append("</td>");
                sb.Append("<td class=tag>").Append(E(n.Source ?? "—")).Append("</td></tr>");
            }
            sb.Append("</table></details>");
        }

        sb.Append("<button>").Append(E(S("btn_save", []))).Append("</button></form>");
    }

    private static void RenderBrowser(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("browser_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("browser_lede", []))).Append("</p>");
        sb.Append("<dl class=kv>");
        sb.Append("<dt>SOCKS5</dt><dd>127.0.0.1:").Append(cfg.MixedPort).Append("</dd>");
        sb.Append("<dt>HTTP</dt><dd>127.0.0.1:").Append(cfg.MixedPort).Append("</dd>");
        sb.Append("</dl>");
        sb.Append("<p class=hint>").Append(E(S("browser_howto", new object[] { cfg.MixedPort }))).Append("</p>");
        sb.Append("<p class=hint>").Append(E(S("browser_note", []))).Append("</p></section>");
    }

    private static readonly (LogView View, string Key, string Query)[] LogViews =
    {
        (LogView.All, "log_view_all", "all"),
        (LogView.Ours, "log_view_ours", "ours"),
        (LogView.Engine, "log_view_engine", "engine"),
        (LogView.Crashes, "log_view_crashes", "crashes"),
    };

    private static void RenderLog(
        StringBuilder sb, CehoConfig cfg, LogView view, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("log_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("log_lede", []))).Append("</p>");

        var crashes = Log.Crashes();
        if (crashes.Count > 0)
        {
            var last = crashes[0];
            sb.Append("<div class=\"flash err\"><b>")
              .Append(E(S("log_crashes", new object[] { crashes.Count }))).Append("</b><br>")
              .Append(E(S("log_crash_last", new object[] { last.When.ToString("dd.MM HH:mm"), last.Context })))
              .Append("</div>");
            sb.Append("<pre class=logbox>");
            foreach (var line in last.Lines.Take(20)) sb.Append(E(line)).Append('\n');
            sb.Append("</pre>");
        }
        else sb.Append("<p class=hint>").Append(E(S("log_no_crashes", []))).Append("</p>");

        // Журнал один, поэтому вид — это фильтр по нему, а не другой файл.
        sb.Append("<div class=row>");
        foreach (var (v, key, query) in LogViews)
            sb.Append("<a class=\"pill").Append(v == view ? " on" : "")
              .Append("\" href=\"/?tab=log&view=").Append(query).Append("\">")
              .Append(E(S(key, []))).Append("</a>");
        sb.Append("</div>");

        var lines = Log.Tail(200, view);
        if (lines.Count == 0)
            sb.Append("<p class=empty>").Append(E(S("log_empty", []))).Append("</p>");
        else
        {
            sb.Append("<pre class=logbox>");
            foreach (var line in lines) sb.Append(E(line)).Append('\n');
            sb.Append("</pre>");
        }

        var current = LogViews.First(v => v.View == view).Query;
        sb.Append("<p class=hint><a href=\"/log/download?view=").Append(current).Append("\">")
          .Append(E(S("log_download", []))).Append("</a> · ").Append(E(Log.FilePath ?? "")).Append("</p>");

        sb.Append("<form class=row method=post action=/log/clear><input type=hidden name=tab value=log>")
          .Append("<button class=danger>").Append(E(S("log_clear", []))).Append("</button></form>");

        sb.Append("<form class=row method=post action=/log/level><input type=hidden name=tab value=log>");
        sb.Append("<span style=\"align-self:center\">").Append(E(S("log_level", []))).Append(":</span>");
        sb.Append("<select name=level style=\"flex:0 0 160px\">");
        foreach (var level in new[] { "warn", "info", "debug", "error" })
            sb.Append("<option value=").Append(level)
              .Append(cfg.EngineLogLevel == level ? " selected" : "").Append('>')
              .Append(level).Append("</option>");
        sb.Append("</select><button class=ghost>").Append(E(S("btn_save", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("log_level_hint", []))).Append("</p></section>");
    }

    private static void RenderAccess(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("nav_access", []))).Append("</h2>");

        var hasPassword = Auth.HasPassword(cfg);
        sb.Append("<p class=lede>")
          .Append(E(S(hasPassword ? "auth_hint_set" : "auth_no_password", []))).Append("</p>");
        sb.Append("<div class=\"status ").Append(hasPassword ? "on" : "bad").Append("\"><span class=dot></span><b>")
          .Append(E(S(hasPassword ? "auth_is_set" : "auth_not_set", []))).Append("</b></div>");

        sb.Append("<form class=row method=post action=/password><input type=hidden name=tab value=access>");
        sb.Append("<input type=password name=password placeholder=\"").Append(E(S("auth_password", []))).Append("\">");
        sb.Append("<input type=password name=password2 placeholder=\"")
          .Append(E(S("setup_password_again", []))).Append("\">");
        sb.Append("<button>").Append(E(S("btn_save", []))).Append("</button></form>");
        if (Auth.HasPassword(cfg))
        {
            sb.Append("<form class=row method=post action=/password><input type=hidden name=tab value=access>")
              .Append("<input type=hidden name=clear value=1>")
              .Append("<button class=danger>").Append(E(S("auth_remove", []))).Append("</button></form>");
        }
        sb.Append("</section>");

        sb.Append("<section><h2>").Append(E(S("lang_title", []))).Append("</h2>");
        sb.Append("<form class=row method=post action=/lang><input type=hidden name=tab value=access>");
        sb.Append("<select name=lang style=\"flex:0 0 200px\">");
        foreach (var l in Strings.Languages)
            sb.Append("<option value=").Append(l).Append(cfg.Language == l ? " selected" : "").Append('>')
              .Append(l == "ru" ? "Русский" : "English").Append("</option>");
        sb.Append("</select><button>").Append(E(S("btn_save", []))).Append("</button></form></section>");

        sb.Append("<section><h2>").Append(E(S("uninstall_title", []))).Append("</h2>");
        sb.Append("<p class=hint>").Append(E(S("uninstall_hint", []))).Append("</p>");
        sb.Append("<form class=row method=post action=/uninstall onsubmit=\"return confirm('")
          .Append(E(S("uninstall_confirm_js", [])))
          .Append("');\"><input type=hidden name=tab value=access>")
          .Append("<button class=danger>").Append(E(S("btn_uninstall", [])))
          .Append("</button></form></section>");
    }

    private static void RenderHelp(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("help_title", []))).Append("</h2><ol class=steps>");
        sb.Append("<li>").Append(E(S("help_1", []))).Append("</li>");
        sb.Append("<li>").Append(E(Os.Kind switch
        {
            OsKind.Windows => S("help_2_win", []),
            OsKind.Mac => S("help_2_mac", []),
            _ => S("help_2_linux", []),
        })).Append("</li>");
        sb.Append("<li>").Append(E(S("help_3", []))).Append("</li>");
        sb.Append("<li>").Append(E(S("help_4", []))).Append("</li></ol>");
        var sudo = Os.IsWindows ? "" : "sudo ";
        sb.Append("<p class=hint>").Append(E(S("help_cli", new object[] { sudo }))).Append("</p>");
        sb.Append("<p class=hint>").Append(E(S("help_doctor", new object[] { sudo }))).Append("</p>");
        sb.Append("<p class=hint>").Append(E(S("help_multiuser", []))).Append("</p></section>");
    }
}

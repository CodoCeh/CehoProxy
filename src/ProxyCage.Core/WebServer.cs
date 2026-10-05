using System.Globalization;
using System.Net;
using System.Text;
using System.Web;

namespace ProxyCage.Core;

public sealed partial class WebServer
{
    private sealed record JobPayload(
        string State, int Percent, string Stage, string? Result, bool IsError,
        bool Relaunch, double Seconds, string? Id = null, bool Indeterminate = false,
        double StageSeconds = 0, double IdleSeconds = 0, bool IsSlow = false,
        DateTime? LastUpdatedUtc = null, bool CanCancel = false, string? Phase = null, DateTime? StageStartedUtc = null, bool Waiting = false, int StartupStep = 0, long Revision = 0);

    private const string CookieName = "ceho";

    private const string JobPool = "pool";
    private const string JobMeasure = "measure";
    private const string JobApply = "apply";
    private const string JobRestore = "restore";
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
    private IReadOnlyDictionary<string, int>? _liveLatency;
    private DateTime _liveLatencyAtUtc;

    public Func<IStageReport, Task<IReadOnlyList<ProxyNode>>>? OnPool { get; set; }

    // Пул держим здесь: страницу нельзя заставлять ждать скачивания подписок.
    private IReadOnlyList<ProxyNode>? _pool;
    private DateTime _poolAtUtc;
    private string? _poolError;

    public Func<IStageReport, Task<string?>>? OnStart { get; set; }
    public Func<Task<string?>>? OnStop { get; set; }
    public Func<IStageReport, Task<string?>>? OnRestart { get; set; }
    public Func<IStageReport, Task<string>>? OnApply { get; set; }
    public Func<bool, DateTime?, IStageReport, Task<string>>? OnRestoreVerified { get; set; }

    public Func<bool, IStageReport, Task<string>>? OnUpdate { get; set; }

    public Func<Task<string?>>? OnElevate { get; set; }

    public Func<IStageReport, Task<string>>? OnEngineUpdate { get; set; }

    public Func<IStageReport, Task<string>>? OnCheckSubs { get; set; }
    public Func<Task<string>>? OnUninstall { get; set; }

    /// <summary>Спросить у выхода страну и адрес: нужно доктору, чтобы убедиться, что защита работает.</summary>
    public Func<Task<(string? Country, string? Ip)>>? OnExit { get; set; }

    // Последний осмотр держим здесь: страница показывает его сразу, без ожидания проверок.
    private Doctor.Result? _doctor;
    private DateTime _doctorAtUtc;
    private ConnPingReport? _lastPing;

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
        if (!IsSameOriginRequest(ctx.Request))
        {
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
            return;
        }
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
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

        if (path == "/icon")
        {
            await HandleIconAsync(ctx, cfg);
            return;
        }

        if (path == "/settings/export" && ctx.Request.HttpMethod == "POST")
        {
            await HandleSettingsExportAsync(ctx, cfg);
            return;
        }

        if (path is "/support/preview" or "/support/download" && ctx.Request.HttpMethod == "POST")
        {
            await HandleSupportReportAsync(ctx, cfg, path);
            return;
        }

        if (path == "/apps/tunnel-state" && ctx.Request.HttpMethod == "GET")
        {
            await HandleTunnelStateAsync(ctx, cfg);
            return;
        }

        if (ctx.Request.HttpMethod == "POST")
        {
            var form = await ReadFormAsync(ctx.Request);
            if (IsAppAddPath(path))
            {
                await HandleAppAddPostAsync(ctx, path, form);
                return;
            }
            var (msg, isError, jobId) = await ApplyPostAsync(path, form, cfg);
            var tab = form.GetValueOrDefault("tab", "state");
            var logView = form.GetValueOrDefault("view", "");
            var q = $"?tab={Uri.EscapeDataString(tab)}";
            if (logView.Length > 0) q += $"&view={Uri.EscapeDataString(logView)}";
            if (msg is not null) q += $"&m={Uri.EscapeDataString(msg)}&e={(isError ? 1 : 0)}";
            if (path == "/apps/check" && !isError) q += "&notice=1";
            if (jobId is not null) q += $"&job={Uri.EscapeDataString(jobId)}";
            if (form.GetValueOrDefault("wizard") is { Length: > 0 } step) q += $"&wizard={Uri.EscapeDataString(step)}";
            Redirect(ctx, "/" + q);
            return;
        }

        // Скачать журнал — GET. POST /log/clear и /log/level должны попасть в форму выше:
        // раньше этот разбор стоял первым и отдавал 404, панель «падала».
        if (path == "/log/download")
        {
            await HandleLogFileAsync(ctx);
            return;
        }

        if (path == "/apps/pick")
        {
            var pick = AppPathPicker.Pick(cfg.Language, Root, cfg.WebPort);
            var q = "?tab=apps";
            if (pick.Path is { Length: > 0 })
            {
                // Native selection identifies a path; adding it still needs the user's review.
                q += "&pick_path=" + Uri.EscapeDataString(AppIdentity.Normalize(pick.Path));
            }
            else if (pick.ErrorKey is { Length: > 0 })
                q += $"&m={Uri.EscapeDataString(Strings.T(cfg.Language, pick.ErrorKey))}&e=1";
            Redirect(ctx, "/" + q);
            return;
        }

        var flash = ctx.Request.QueryString["m"];
        var flashErr = ctx.Request.QueryString["e"] == "1";
        var current = ctx.Request.QueryString["tab"] ?? "state";
        var job = Jobs.Find(ctx.Request.QueryString["job"]) ?? Jobs.Active(JobPower) ?? Jobs.Active(JobApply) ?? Jobs.Active(JobRestore);
        if (job is { State: JobState.Done, IsError: false })
        {
            var q = ctx.Request.QueryString;
            var message = string.Join(" ", new[] { flash, job.Result }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var parts = q.AllKeys
                .Where(k => k is not null and not "job" and not "m" and not "e")
                .Select(k => $"{Uri.EscapeDataString(k!)}={Uri.EscapeDataString(q[k] ?? "")}")
                .Append("m=" + Uri.EscapeDataString(message)).Append("e=0");
            Redirect(ctx, "/?" + string.Join("&", parts));
            return;
        }
        var view = ViewFromQuery(ctx.Request.QueryString["view"]);
        var tunnel = ctx.Request.QueryString["tunnel"];
        var st = _state();
        if (current == "exit" && st.Running)
            await RefreshLiveLatencyAsync(cfg);
        await WriteHtmlAsync(ctx, RenderPage(cfg, st, current, flash, flashErr, job, view, tunnel,
            ctx.Request.QueryString["wizard"], ctx.Request.QueryString["pick_path"]));
    }

    private static bool IsSameOriginRequest(HttpListenerRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"];
        if (site is "cross-site" or "same-site") return false;
        var source = request.Headers["Origin"] ?? request.Headers["Referer"];
        if (source is null) return true; // CLI clients do not send browser metadata.
        return Uri.TryCreate(source, UriKind.Absolute, out var origin)
            && request.Url is { } target
            && origin.Scheme == target.Scheme
            && origin.Host == target.Host
            && origin.Port == target.Port;
    }

    private static bool Authorized(HttpListenerContext ctx, CehoConfig cfg)
    {
        if (!Auth.HasPassword(cfg)) return true;
        var cookie = ctx.Request.Cookies[CookieName]?.Value;
        return Auth.ValidSession(cfg, cookie);
    }

    private async Task HandleGateAsync(HttpListenerContext ctx, CehoConfig cfg, string path)
    {
        string? error = null;

        if (ctx.Request.HttpMethod == "POST" && path == "/login")
        {
            var form = await ReadFormAsync(ctx.Request);
            var check = Auth.Check(cfg, form.GetValueOrDefault("password", ""));
            if (check.Ok)
            {
                var token = Auth.IssueSession(cfg);

                ctx.Response.Headers.Add("Set-Cookie",
                    $"{CookieName}={token}; Path=/; HttpOnly; SameSite=Strict");
                Redirect(ctx, "/");
                return;
            }
            error = check.Locked
                ? Strings.T(cfg.Language, "auth_locked", check.RetrySeconds)
                : Strings.T(cfg.Language, "auth_wrong");
        }

        await WriteHtmlAsync(ctx, RenderGate(cfg, error));
    }

    public Func<string[], Task<(bool Ok, string Text)>>? OnApiCommand { get; set; }

    private async Task HandleApiAsync(HttpListenerContext ctx, CehoConfig cfg)
    {
        if (ctx.Request.HttpMethod != "POST") { ctx.Response.StatusCode = 405; ctx.Response.Close(); return; }

        var check = Auth.Check(cfg, Auth.FromHeaders(
            ctx.Request.Headers[Auth.PasswordHeader], ctx.Request.Headers[Auth.PasswordHeaderBase64]));
        if (!check.Ok)
        {
            ctx.Response.StatusCode = check.Locked ? 429 : 401;
            if (check.Locked) ctx.Response.Headers["Retry-After"] = check.RetrySeconds.ToString();
            await WriteJsonAsync(ctx, false, check.Locked
                ? Strings.T(cfg.Language, "auth_locked", check.RetrySeconds)
                : Strings.T(cfg.Language, "auth_wrong"));
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

        await _configGate.WaitAsync();
        try
        {
            if (Jobs.Active(JobRestore) is not null)
            { await WriteJsonAsync(ctx, false, "Configuration recovery is running; retry after it finishes."); return; }
            var (ok, text) = await OnApiCommand(argv);
            await WriteJsonAsync(ctx, ok, text);
        }
        finally { _configGate.Release(); }
    }

    private async Task HandleSupportReportAsync(HttpListenerContext ctx, CehoConfig cfg, string path)
    {
        if (ctx.Request.ContentLength64 > 512 * 1024)
        { ctx.Response.StatusCode = 413; ctx.Response.Close(); return; }
        var form = await ReadFormAsync(ctx.Request);
        if (path == "/support/preview")
        {
            var parts = SupportReport.PartsFromForm(form);
            var report = SupportReport.Create(cfg, parts, _state(), _doctor,
                parts.HasFlag(SupportReport.Parts.Logs) ? Log.Tail(100) : null);
            await WriteHtmlAsync(ctx, SafetyPanel.RenderReportPreview(cfg, report, parts));
            return;
        }
        var preview = form.GetValueOrDefault("report", "");
        if (Encoding.UTF8.GetByteCount(preview) > 128 * 1024 || !preview.StartsWith("CehoProxy support report\n", StringComparison.Ordinal))
        { ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
        var bytes = Encoding.UTF8.GetBytes(preview);
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + SupportReport.FileName + "\"";
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private async Task HandleJobAsync(HttpListenerContext ctx)
    {
        var id = ctx.Request.QueryString["id"];
        var job = Jobs.Find(id);
        var handoff = UpdateHandoff.Read(Root);
        JobPayload payload;

        var handoffStageReached = job is null
            || (job.Kind == JobUpdate && job.RelaunchPanel && job.Percent >= 90);
        if (handoff is not null && UpdateHandoff.MatchesJob(handoff, id, handoffStageReached))
        {
            payload = handoff.State switch
            {
                "verified" => new JobPayload(
                    "done", 100, handoff.Message, handoff.Message, false, true,
                    Math.Round(job?.Elapsed.TotalSeconds ?? 0, 1)),
                "failed" => new JobPayload(
                    "failed", 100, handoff.Message, handoff.Message, true, false,
                    Math.Round(job?.Elapsed.TotalSeconds ?? 0, 1)),
                _ => new JobPayload(
                    "running", Math.Max(95, job?.Percent ?? 95), handoff.Message, null, false, true,
                    Math.Round(job?.Elapsed.TotalSeconds ?? 0, 1)),
            };
        }
        else if (job is null)
            payload = new JobPayload("gone", 100, "", null, false, false, 0);
        else
        {
            var snapshot = job.Snapshot();
            payload = new JobPayload(
                snapshot.State switch { JobState.Running => "running", JobState.Done => "done", _ => "failed" },
                snapshot.Percent, snapshot.Stage, snapshot.Result, snapshot.IsError, snapshot.RelaunchPanel,
                Math.Round(snapshot.Seconds, 1), snapshot.Id, snapshot.Indeterminate,
                snapshot.StageSeconds, snapshot.IdleSeconds, snapshot.IsSlow, snapshot.LastUpdatedUtc,
                snapshot.CanCancel, snapshot.Phase, snapshot.StageStartedUtc, snapshot.Waiting, snapshot.StartupStep, snapshot.Revision);
        }

        payload = payload with { Id = id };
        var json = System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
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
    private async Task HandleSettingsExportAsync(HttpListenerContext ctx, CehoConfig cfg)
    {
        var form = await ReadFormAsync(ctx.Request);
        var password = form.GetValueOrDefault("password", "");
        string? error = password.Length < 6 ? "transfer_password_short"
            : password != form.GetValueOrDefault("password2", "") ? "setup_password_mismatch"
            : null;
        if (error is not null)
        {
            Redirect(ctx, "/?tab=access&m=" + Uri.EscapeDataString(Strings.T(cfg.Language, error)) + "&e=1");
            return;
        }

        var parts = PartsFromForm(form);
        if (parts == SettingsTransfer.Parts.None)
        {
            Redirect(ctx, "/?tab=access&m=" + Uri.EscapeDataString(Strings.T(cfg.Language, "transfer_no_parts")) + "&e=1");
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(SettingsTransfer.Export(Root, password, parts));
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{SettingsTransfer.FileName}\"");
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
        Log.Info(Strings.T(cfg.Language, "transfer_exported"));
    }

    private async Task HandleLogFileAsync(HttpListenerContext ctx)
    {
        var view = ViewFromQuery(ctx.Request.QueryString["view"]);
        var text = string.Join(Environment.NewLine, Log.Tail(5000, view));
        var bytes = Encoding.UTF8.GetBytes(text.Length == 0 ? "журнал пуст" : text);

        var name = view switch
        {
            LogView.Engine => "cehoproxy-движок.log",
            LogView.Crashes => "cehoproxy-падения.log",
            LogView.Important => "cehoproxy-важное.log",
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
        "important" => LogView.Important,
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

    private int _pending;
    private readonly SemaphoreSlim _configGate = new(1, 1);

    private string? ApplyOrDefer(CehoConfig cfg, string? doneMessage = null, bool restartIfRunning = false)
    {
        Interlocked.Increment(ref _pending);
        if (!_state().Running && Jobs.Active(JobApply) is null && Jobs.Active(JobPower) is null)
            return ApplyJob(cfg, doneMessage, restartIfRunning).Id;
        return null;
    }

    private Job ApplyJob(CehoConfig cfg, string? doneMessage = null, bool restartIfRunning = false, bool allowRestart = false) =>
        StartApplyJob(cfg, doneMessage, restartIfRunning, allowRestart);

    private Job StartApplyJob(CehoConfig cfg, string? doneMessage, bool restartIfRunning, bool allowRestart) =>
        Jobs.Start(JobApply, Strings.T(cfg.Language, restartIfRunning && _state().Running ? "job_restart" : "job_apply"), async p =>
        {
            InvalidateAppObservations();
            var before = ConfigFingerprint();
            var pendingBefore = Volatile.Read(ref _pending);
            try
            {
                // Admission is not enough: a queued startup may have completed since the click.
                if (_state().Running && !allowRestart)
                    throw new InvalidOperationException(ConfirmApplyMessage(cfg));
                string applied;
                var didApply = false;
                if (OnApplyWithRestartConfirmation is not null)
                {
                    applied = await OnApplyWithRestartConfirmation(allowRestart, p);
                    didApply = true;
                }
                else if (restartIfRunning && _state().Running && OnRestart is not null)
                {
                    var err = await OnRestart(p);
                    if (err is not null) throw new InvalidOperationException(err);
                    applied = Strings.T(cfg.Language, "rules_applied");
                    didApply = true;
                }
                else
                {
                    applied = OnApply is null ? Strings.T(cfg.Language, "rules_rebuilt") : await OnApply(p);
                    didApply = OnApply is not null;
                }
                if (didApply) RecordAppliedRules(before, pendingBefore);
                return doneMessage is null ? applied : $"{doneMessage} {applied}";
            }
            catch { ForgetAppliedRules(); throw; }
            finally { InvalidateAppObservations(); }
        }, rerunIfBusy: false, operationIdentity: allowRestart ? "confirmed-apply" : "deferred-apply");

    private Job StopJob(CehoConfig cfg, bool removeLastApp = false) =>
        Jobs.Start(JobPower, Strings.T(cfg.Language, "job_stop"), async p =>
        {
            InvalidateAppObservations();
            try
            {
            p.Phase(Strings.T(cfg.Language, "stage_stopping"));
            var before = removeLastApp ? ConfigFingerprint() : null;
            var pendingBefore = Volatile.Read(ref _pending);
            var err = removeLastApp && OnRemoveLastApp is not null
                ? await OnRemoveLastApp(cfg)
                : OnStop is null ? "no control" : await OnStop();
            if (err is not null) throw new InvalidOperationException(err);
            if (removeLastApp && OnRemoveLastApp is not null) RecordAppliedRules(before, pendingBefore);
            return Strings.T(cfg.Language, "state_off");
            } finally { InvalidateAppObservations(); }
        }, operationIdentity: removeLastApp ? "remove-last-app" : "stop");

    private async Task<(string? Message, bool IsError, string? JobId)> ApplyPostAsync(
        string path, Dictionary<string, string> f, CehoConfig cfg)
    {
        await _configGate.WaitAsync();
        try { return await ApplyPostCoreAsync(path, f, CehoConfig.Load(_configPath)); }
        finally { _configGate.Release(); }
    }

    private async Task<(string? Message, bool IsError, string? JobId)> ApplyPostCoreAsync(
        string path, Dictionary<string, string> f, CehoConfig cfg)
    {
        string S(string key, params object[] a) => Strings.T(cfg.Language, key, a);

        try
        {
            // Recovery is a user-confirmed replacement. Later edits must not disappear into it.
            if (Jobs.Active(JobRestore) is { } restoring)
                return (cfg.Language == "ru" ? "Сначала дождитесь завершения восстановления настроек." : "Wait for configuration recovery to finish before making another change.", false, restoring.Id);
            switch (path)
            {
                case "/settings/restore":
                {
                    if (f.GetValueOrDefault("confirm_restore") != "1")
                        throw new InvalidOperationException(cfg.Language == "ru" ? "Подтвердите возврат конфигурации." : "Confirm configuration recovery.");
                    if (OnRestoreVerified is null)
                        throw new InvalidOperationException(cfg.Language == "ru" ? "Восстановление недоступно в этой службе." : "Configuration recovery is unavailable in this service.");
                    if (Jobs.Running().Count > 0)
                        throw new InvalidOperationException(cfg.Language == "ru" ? "Перед восстановлением дождитесь завершения текущих проверок и действий." : "Wait for current checks and operations before restoring configuration.");
                    var acknowledge = f.GetValueOrDefault("acknowledge_security") == "1";
                    if (!DateTime.TryParse(f.GetValueOrDefault("verified_utc"), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var expectedVerifiedUtc))
                        throw new InvalidOperationException(cfg.Language == "ru" ? "Обновите предпросмотр конфигурации." : "Refresh the configuration preview.");
                    var job = Jobs.Start(JobRestore, cfg.Language == "ru" ? "Возвращаем рабочие настройки" : "Restoring working configuration",
                        async p => { InvalidatePanelData(); try { var result = await OnRestoreVerified(acknowledge, expectedVerifiedUtc, p); Interlocked.Exchange(ref _pending, 0); return result; } finally { InvalidatePanelData(); } });
                    return (null, false, job.Id);
                }

                case "/apps/add":
                case "/apps/detected":
                case "/apps/installed":
                    return AddAppCore(cfg, path, f).Legacy;

                case "/apps/bounce":
                {
                    var report = IsolatedAppBounce.ResetNetwork(cfg, _log);
                    var n = report.Killed + report.Connections;
                    return n > 0
                        ? (S("bounced_network", n, string.Join(", ", report.Labels)), false, null)
                        : (S("bounced_network_none"), false, null);
                }

                case "/apps/remove":
                {
                    // Admission and the save share _configGate. A last-app cleanup must
                    // not be rejected by a power job after the saved app was already removed.
                    if (Jobs.Active(JobPower) is { } activePower)
                        return (Strings.T(cfg.Language, "job_conflict", activePower.Title), true, activePower.Id);
                    var folder = f.GetValueOrDefault("folder", "");
                    cfg.Apps.RemoveAll(a => AppIdentity.SameConfiguredPath(a.Folder, folder));
                    Save(cfg);
                    var job = cfg.Apps.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder))
                        ? ApplyOrDefer(cfg, restartIfRunning: true)
                        : OnRemoveLastApp is not null || (_state().Running && OnStop is not null)
                            ? StopJob(cfg, removeLastApp: true).Id : null;
                    return (S("removed"), false, job);
                }

                case "/sites/mode":
                {
                    var next = string.Equals(f.GetValueOrDefault("mode", ""), CehoConfig.SiteModeOnly,
                        StringComparison.OrdinalIgnoreCase)
                        ? CehoConfig.SiteModeOnly
                        : CehoConfig.SiteModeExcept;
                    if (string.Equals(cfg.SiteMode, next, StringComparison.OrdinalIgnoreCase))
                        return (S("site_mode_set", S(next == CehoConfig.SiteModeOnly ? "sites_mode_only" : "sites_mode_except")), false, null);

                    cfg.SiteMode = next;
                    Save(cfg);
                    var modeJob = cfg.Apps.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder))
                        ? ApplyOrDefer(cfg, restartIfRunning: true)
                        : null;
                    return (S("site_mode_set", S(next == CehoConfig.SiteModeOnly ? "sites_mode_only" : "sites_mode_except")), false, modeJob);
                }

                case "/sites/add":
                {
                    var host = DirectSites.Normalize(f.GetValueOrDefault("site", ""));
                    if (host is null) return (S("site_bad"), true, null);
                    if (cfg.DirectSites.Contains(host, StringComparer.OrdinalIgnoreCase))
                        return (S("err_already_added"), true, null);
                    cfg.DirectSites.Add(host);
                    var country = DirectSites.NormalizeCountry(f.GetValueOrDefault("country", ""));
                    if (country is not null) cfg.SiteCountries[host] = country;
                    Save(cfg);
                    var job = cfg.Apps.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder))
                        ? ApplyOrDefer(cfg, restartIfRunning: true)
                        : null;
                    return (S("site_added", host), false, job);
                }

                case "/sites/preset":
                {
                    var fresh = DirectSites.NewHosts(cfg.DirectSites, DirectSites.Preset(cfg.SitesOnly));
                    if (fresh.Count == 0) return (S("sites_preset_none"), false, null);
                    cfg.DirectSites.AddRange(fresh);
                    Save(cfg);
                    var presetJob = cfg.Apps.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder))
                        ? ApplyOrDefer(cfg, restartIfRunning: true)
                        : null;
                    return (S("sites_preset_added", fresh.Count), false, presetJob);
                }

                case "/sites/country":
                {
                    var host = cfg.DirectSites.FirstOrDefault(s =>
                        s.Equals(f.GetValueOrDefault("site", ""), StringComparison.OrdinalIgnoreCase));
                    if (host is null) return (S("site_bad"), true, null);
                    var country = DirectSites.NormalizeCountry(f.GetValueOrDefault("country", ""));
                    var current = SingBoxConfigGenerator.SiteCountry(cfg, host);
                    if (string.Equals(current, country, StringComparison.OrdinalIgnoreCase))
                    {
                        var same = country is null
                            ? S(cfg.SitesOnly ? "sites_exit_pool" : "sites_exit_direct", [])
                            : CountryResolver.DisplayName(country, cfg.Language) ?? country;
                        return (S("sites_exit_saved", host, same), false, null);
                    }

                    if (country is null)
                        cfg.SiteCountries.Remove(host);
                    else
                        cfg.SiteCountries[host] = country;
                    Save(cfg);
                    var countryJob = cfg.Apps.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder))
                        ? ApplyOrDefer(cfg, restartIfRunning: true)
                        : null;
                    var label = country is null
                        ? S(cfg.SitesOnly ? "sites_exit_pool" : "sites_exit_direct", [])
                        : CountryResolver.DisplayName(country, cfg.Language) ?? country;
                    return (S("sites_exit_saved", host, label), false, countryJob);
                }

                case "/sites/remove":
                {
                    var host = f.GetValueOrDefault("site", "");
                    cfg.DirectSites.RemoveAll(s => s.Equals(host, StringComparison.OrdinalIgnoreCase));
                    foreach (var key in cfg.SiteCountries.Keys
                                 .Where(k => k.Equals(host, StringComparison.OrdinalIgnoreCase)).ToList())
                        cfg.SiteCountries.Remove(key);
                    Save(cfg);
                    var job = cfg.Apps.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder))
                        ? ApplyOrDefer(cfg, restartIfRunning: true)
                        : null;
                    return (S("removed"), false, job);
                }

                case "/settings/import":
                {
                    var data = f.GetValueOrDefault("data", "");
                    if (data.Length == 0) return (S("transfer_no_file"), true, null);
                    var parts = PartsFromForm(f);
                    if (parts == SettingsTransfer.Parts.None) return (S("transfer_no_parts"), true, null);
                    try
                    {
                        var result = SettingsTransfer.Import(Root, data, f.GetValueOrDefault("password", ""), parts);
                        _pool = null;
                        var fresh = CehoConfig.Load(_configPath);
                        var text = SettingsTransfer.Describe(result, fresh.Language);
                        Log.Info(text);
                        return (text, false, ApplyOrDefer(fresh, restartIfRunning: true));
                    }
                    catch (SettingsTransfer.WrongPasswordException) { return (S("transfer_wrong_password"), true, null); }
                    catch (InvalidDataException) { return (S("transfer_bad_file"), true, null); }
                }

                case "/apps/check":
                {
                    var folder = f.GetValueOrDefault("folder", "");
                    var app = cfg.Apps.FirstOrDefault(a =>
                        AppIdentity.SameConfiguredPath(a.Folder, folder));
                    if (app is null) return (S("app_tunnel_missing"), true, null);
                    return CheckApp(cfg, app);
                }

                case "/apps/rename":
                {
                    var folder = f.GetValueOrDefault("folder", "");
                    var app = cfg.Apps.FirstOrDefault(a =>
                        AppIdentity.SameConfiguredPath(a.Folder, folder));
                    if (app is null) return (S("app_tunnel_missing"), true, null);

                    var displayName = f.GetValueOrDefault("displayName", "").Trim();
                    app.DisplayName = displayName.Length > 0 ? displayName : null;
                    cfg.SaveSubscriptionStatus(_configPath);
                    return (S("app_renamed", app.Label), false, null);
                }

                case "/apps/tunnel":
                {
                    var folder = f.GetValueOrDefault("folder", "");
                    var app = cfg.Apps.FirstOrDefault(a =>
                        AppIdentity.SameConfiguredPath(a.Folder, folder));
                    if (app is null) return (S("app_tunnel_missing"), true, null);

                    var shown = (f.GetValueOrDefault("all", "") ?? "")
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim())
                        .Where(k => k.Length > 0)
                        .ToList();
                    var keep = f.Keys.Where(k => k.StartsWith("n_", StringComparison.Ordinal))
                        .Select(k => k[2..])
                        .Where(k => shown.Contains(k, StringComparer.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    app.AllowedNodes = keep;
                    Save(cfg);

                    var msg = keep.Count == 0
                        ? S("app_tunnel_cleared", app.Label)
                        : S("app_tunnel_saved", app.Label, keep.Count);
                    return (msg, false, ApplyOrDefer(cfg, restartIfRunning: true));
                }

                case "/apps/add-recommended":
                {
                    var added = 0;
                    foreach (var entry in InstalledAppCatalog.Recommended().Where(e => !AppCoverage.IsEntryCovered(cfg, e.Path)).ToList())
                    {
                        var res = AddAppCore(cfg, "/apps/installed", new Dictionary<string, string>
                        { ["path"] = entry.Path, ["intent"] = "tunnel", ["confirm_add"] = "1" });
                        if (res.Status == "added") added++;
                    }
                    return added > 0 ? (S("rec_added", added), false, null) : (S("rec_none"), false, null);
                }

                case "/apps/node-next":
                case "/apps/node-word":
                case "/apps/node-reset":
                {
                    var folder = f.GetValueOrDefault("folder", "");
                    var app = cfg.Apps.FirstOrDefault(a => AppIdentity.SameConfiguredPath(a.Folder, folder));
                    if (app is null) return (S("app_tunnel_missing"), true, null);
                    app.UnsuitableNodes ??= [];
                    var pool = _pool ?? [];
                    if (path == "/apps/node-reset")
                    {
                        app.AllowedNodes = [];
                        app.UnsuitableNodes = [];
                        Save(cfg);
                        return (S("node_reset_done", app.Label), false, ApplyOrDefer(cfg, restartIfRunning: true));
                    }
                    if (pool.Count == 0) return (S("node_pool_empty"), true, null);
                    if (path == "/apps/node-word")
                    {
                        var word = f.GetValueOrDefault("word", "").Trim();
                        var matched = NodeChoice.ByWord(pool.Where(n => !cfg.BlockedNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase)), word);
                        if (matched.Count == 0) return (S("node_word_none", word), true, null);
                        app.AllowedNodes = matched.Select(n => n.Key).ToList();
                        Save(cfg);
                        return (S("node_word_set", app.Label, word, matched.Count), false, ApplyOrDefer(cfg, restartIfRunning: true));
                    }
                    var next = NodeChoice.Next(app, pool, cfg);
                    if (next is null) return (S("node_none_left"), true, null);
                    foreach (var key in app.AllowedNodes)
                        if (!app.UnsuitableNodes.Contains(key, StringComparer.OrdinalIgnoreCase)) app.UnsuitableNodes.Add(key);
                    app.AllowedNodes = [next.Key];
                    Save(cfg);
                    var label = (next.Remark.Length > 0 ? next.Remark : next.Tag) + (next.CountryName is { Length: > 0 } cn ? " · " + cn : "");
                    return (S("node_next_set", app.Label, label), false, ApplyOrDefer(cfg, restartIfRunning: true));
                }

                case "/apps/country":
                {
                    var folder = f.GetValueOrDefault("folder", "");
                    var app = cfg.Apps.FirstOrDefault(a => AppIdentity.SameConfiguredPath(a.Folder, folder));
                    if (app is null) return (S("app_tunnel_missing"), true, null);
                    var code = f.GetValueOrDefault("country", "").Trim();
                    if (code.Length == 0)
                    {
                        app.AllowedNodes = [];
                        Save(cfg);
                        return (S("app_tunnel_cleared", app.Label), false, ApplyOrDefer(cfg, restartIfRunning: true));
                    }
                    var nodes = (_pool ?? []).Where(n => !n.IsMeta
                        && string.Equals(n.CountryCode ?? CountryResolver.Unknown, code, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (nodes.Count == 0) return (S("country_missing"), true, null);
                    app.AllowedNodes = nodes.Select(n => n.Key).ToList();
                    Save(cfg);
                    return (S("country_set", app.Label, nodes[0].CountryName ?? code), false, ApplyOrDefer(cfg, restartIfRunning: true));
                }

                case "/apps/offline":
                {
                    var folder = f.GetValueOrDefault("folder", "");
                    var app = cfg.Apps.FirstOrDefault(a =>
                        AppIdentity.SameConfiguredPath(a.Folder, folder));
                    if (app is null) return (S("app_tunnel_missing"), true, null);

                    app.NoInternet = f.ContainsKey("enable");
                    Save(cfg);
                    return (S(app.NoInternet ? "app_offline_saved" : "app_offline_cleared", app.Label),
                        false, ApplyOrDefer(cfg, restartIfRunning: true));
                }

                case "/subs/add":
                {
                    var name = f.GetValueOrDefault("name", "").Trim();
                    if (name.Length == 0) name = $"sub{cfg.Subscriptions.Count + 1}";
                    if (cfg.Subscriptions.Any(s => s.Name == name))
                        return (S("err_sub_exists", name), true, null);

                    if (!TryBuildSubUrl(f, name, null, out var url, out var errKey))
                        return (S(errKey!, []), true, null);

                    cfg.Subscriptions.Add(new SubscriptionEntry { Name = name, Url = url! });
                    cfg.ActiveSubscription ??= name;
                    Save(cfg);
                    _pool = null;
                    return (S("sub_added", name), false, ApplyOrDefer(cfg));
                }

                case "/subs/save":
                {
                    var orig = f.GetValueOrDefault("origName", "").Trim();
                    var entry = cfg.Subscriptions.FirstOrDefault(s => s.Name == orig);
                    if (entry is null) return (S("subs_edit_missing", []), true, null);

                    NaiveProxySettings? keepPass = null;
                    if (SubscriptionKind.IsNaive(entry)
                        && NaiveProxyHelper.TryParseUri(entry.Url, out var prev) && prev is not null)
                        keepPass = prev;

                    var name = f.GetValueOrDefault("name", "").Trim();
                    if (name.Length == 0) name = orig;
                    if (cfg.Subscriptions.Any(s => s.Name == name && !string.Equals(s.Name, orig, StringComparison.Ordinal)))
                        return (S("err_sub_exists", name), true, null);

                    if (!TryBuildSubUrl(f, name, keepPass, out var url, out var errKey))
                        return (S(errKey!, []), true, null);

                    if (string.Equals(cfg.ActiveSubscription, orig, StringComparison.Ordinal)
                        && !string.Equals(name, orig, StringComparison.Ordinal))
                        cfg.ActiveSubscription = name;

                    entry.Name = name;
                    entry.Url = url!;
                    Save(cfg);
                    _pool = null;
                    return (S("sub_saved", name), false, ApplyOrDefer(cfg));
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
                    return (S(wanted ? "sub_turned_on" : "sub_turned_off", name), false, ApplyOrDefer(cfg));
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

                case "/exit/save":
                {
                    var prevExcluded = new List<string>(cfg.ExcludedCountries);
                    var prevPreferred = new List<string>(cfg.PreferredCountries);
                    var prevBlocked = new List<string>(cfg.BlockedNodes);
                    var prevLimit = cfg.MaxLatencyMs;

                    if (f.TryGetValue("call", out var countryList))
                    {
                        var all = countryList.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        var on = f.Keys.Where(k => k.StartsWith("c_", StringComparison.Ordinal))
                            .Select(k => k[2..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        cfg.ExcludedCountries = cfg.ExcludedCountries
                            .Where(c => !all.Contains(c, StringComparer.OrdinalIgnoreCase))
                            .Concat(all.Where(c => !on.Contains(c)))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        cfg.PreferredCountries.RemoveAll(c => cfg.ExcludedCountries.Contains(c, StringComparer.OrdinalIgnoreCase));
                    }

                    if (f.TryGetValue("nall", out var nodeList))
                    {
                        var shown = nodeList.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
                        var keep = f.Keys.Where(k => k.StartsWith("n_", StringComparison.Ordinal))
                            .Select(k => k[2..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        cfg.BlockedNodes = cfg.BlockedNodes
                            .Where(k => !shown.Contains(k, StringComparer.OrdinalIgnoreCase))
                            .Concat(shown.Where(k => !keep.Contains(k)))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    }

                    cfg.RotationEnabled = f.ContainsKey("rotation");
                    var checkUrl = f.GetValueOrDefault("checkurl", "").Trim();
                    if (checkUrl.Length > 0) cfg.CheckUrl = checkUrl;
                    if (int.TryParse(f.GetValueOrDefault("timeout", "").Trim(), out var tSec) && tSec is >= 1 and <= 300)
                        cfg.TimeoutSeconds = tSec;
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
                            back.ExcludedCountries = prevExcluded;
                            back.PreferredCountries = prevPreferred;
                            back.BlockedNodes = prevBlocked;
                            back.MaxLatencyMs = prevLimit;
                            Save(back);
                            throw new InvalidOperationException($"{ex.Message} {S("change_reverted")}");
                        }
                    }, rerunIfBusy: true);
                    return (null, false, job.Id);
                }

                case "/browser/test":
                {
                    var port = cfg.MixedPort;
                    var job = Jobs.Start("proxy-test", S("job_naive_test"), async p =>
                    {
                        p.Stage(S("job_naive_test"), 30);
                        await Task.Yield();
                        var r = ProxyProbe.TestMixed(port, null);
                        p.Stage(S("job_naive_test"), 95);
                        return ProxyProbe.FormatResult(r, cfg.Language, null);
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
                        var fresh = CehoConfig.Load(_configPath);
                        var moved = DaemonControl.IsRunning(Root)
                            ? null
                            : Preflight.SaveProxyPortIfBusy(fresh, _configPath);
                        var r = await Doctor.CheckAsync(CehoConfig.Load(_configPath), Root, Tools(), p);
                        Remember(r);
                        var headline = Doctor.Headline(r, CehoConfig.Load(_configPath).Language);
                        return moved is null ? headline : $"{moved} {headline}";
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

                case "/doctor/ping":
                {
                    var job = Jobs.Start("doctor-ping", S("job_ping_test"), async p =>
                    {
                        var fresh = CehoConfig.Load(_configPath);
                        var r = await ConnPing.RunAsync(fresh, (msg, pct) => p.Stage(msg, pct));
                        _lastPing = r;
                        return ConnPing.Summary(r, fresh.Language);
                    });
                    return (null, false, job.Id);
                }

                case "/countries/refresh":
                {
                    if (OnCountries is null) return (S("measure_blocked"), true, null);

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
                        if (fresh.MaxLatencyMs is null) fresh.SaveSubscriptionStatus(_configPath);
                        else Save(fresh);

                        _countries = rows;
                        var byKey = items.GroupBy(m => m.Node.Key, StringComparer.Ordinal)
                            .ToDictionary(g => g.Key, g => g.First().Node, StringComparer.Ordinal);
                        if (_pool is { } pool)
                            foreach (var node in pool)
                                if (byKey.TryGetValue(node.Key, out var measured))
                                {
                                    node.CountryCode = measured.CountryCode;
                                    node.CountryName = measured.CountryName;
                                }
                        return S("measure_done", rows.Count);
                    });
                    return (null, false, job.Id);
                }

                case "/pool/refresh":
                {
                    return (null, false, StartPoolJob(cfg).Id);
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

                case "/mode":
                {
                    cfg.PanelMode = f.GetValueOrDefault("mode") == CehoConfig.PanelModeSimple
                        ? CehoConfig.PanelModeSimple
                        : CehoConfig.PanelModePro;
                    cfg.SaveSubscriptionStatus(_configPath);
                    return (null, false, null);
                }

                case "/lang":
                {
                    cfg.Language = Strings.Normalize(f.GetValueOrDefault("lang", "ru"));
                    cfg.SaveSubscriptionStatus(_configPath);
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
                    if (install) job.RelaunchPanel = true;
                    return (null, false, job.Id);
                }

                case "/engine/update":
                {
                    if (OnEngineUpdate is null) return ("no control", true, null);
                    var job = Jobs.Start(JobEngine, S("job_engine_update"), async p =>
                    {
                        try { return await OnEngineUpdate(p); }
                        finally { ForgetEngineVersion(); }
                    });
                    return (null, false, job.Id);
                }

                case "/engine/autoupdate":
                {
                    cfg.EngineAutoUpdate = f.ContainsKey("enable");
                    cfg.SaveSubscriptionStatus(_configPath);
                    return (S(cfg.EngineAutoUpdate ? "engine_auto_state_on" : "engine_auto_state_off"), false, null);
                }

                case "/tray-controls":
                {
                    cfg.TrayControls = f.ContainsKey("enable");
                    cfg.SaveSubscriptionStatus(_configPath);
                    return (S(cfg.TrayControls ? "tray_controls_on" : "tray_controls_off"), false, null);
                }

                case "/autoupdate":
                {
                    cfg.AutoUpdate = f.ContainsKey("enable");
                    cfg.SaveSubscriptionStatus(_configPath);
                    return (S(cfg.AutoUpdate ? "upd_auto_state_on" : "upd_auto_state_off"), false, null);
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

                case "/apply":
                {
                    var confirmed = f.GetValueOrDefault("confirm_apply") == "1";
                    if (_state().Running && !confirmed) return (ConfirmApplyMessage(cfg), true, null);
                    return (null, false, ApplyJob(cfg, restartIfRunning: true, allowRestart: confirmed).Id);
                }

                case "/elevate":
                {
                    if (OnElevate is null) return (S("elevate_no_way"), true, null);
                    var job = Jobs.Start(JobUpdate, S("job_elevate"), async p =>
                    {
                        var err = await OnElevate();
                        if (err is not null) throw new InvalidOperationException(err);
                        return S("elevate_done");
                    });
                    job.RelaunchPanel = true;
                    return (null, false, job.Id);
                }

                case "/control/start":
                {
                    var job = Jobs.Start(JobPower, S("job_start"), async p =>
                    {
                        InvalidateAppObservations();
                        try
                        {
                        var err = OnStart is null ? "no control" : await OnStart(p);
                        if (err is not null) throw new InvalidOperationException(err);
                        return S("state_on");
                        } finally { InvalidateAppObservations(); }
                    }, operationIdentity: "start");
                    return (null, false, job.Id);
                }

                case "/control/stop":
                {
                    var job = StopJob(cfg);
                    return (null, false, job.Id);
                }

                case "/control/restart":
                {
                    var job = Jobs.Start(JobPower, S("job_restart"), async p =>
                    {
                        InvalidateAppObservations();
                        try
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
                        } finally { InvalidateAppObservations(); }
                    }, operationIdentity: "restart");
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
        catch (JobOperationConflictException ex)
        {
            return (Strings.T(cfg.Language, "job_conflict", ex.ActiveTitle), true, ex.ActiveJobId);
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
        _doctorSimple = false;
    }

    private Job StartPoolJob(CehoConfig cfg) =>
        Jobs.Active(JobRestore) ?? Jobs.Start(JobPool, Strings.T(cfg.Language, "job_pool"), async p =>
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

    public void NotifyEngineStateChanged()
    {
        ForgetAppliedRules();
        InvalidatePanelData();
    }

    private void InvalidatePanelData()
    {
        InvalidateAppObservations();
        _pool = null; _poolError = null; _poolAtUtc = default;
        _countries = null; _liveLatency = null; _liveLatencyAtUtc = default;
        _doctor = null; _doctorAtUtc = default; _lastPing = null;
    }

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

    private async Task HandleIconAsync(HttpListenerContext ctx, CehoConfig cfg)
    {
        var wanted = ctx.Request.QueryString["path"];
        var allowed = wanted is { Length: > 0 } && IconAllowed(cfg, wanted);
        var icon = allowed ? AppIcons.Load(wanted!, cfg.Language) : null;
        if (icon is null)
        {
            ctx.Response.StatusCode = allowed ? 204 : 404;
            ctx.Response.Headers["Cache-Control"] = "private, max-age=600";
            ctx.Response.Close();
            return;
        }

        ctx.Response.ContentType = icon.ContentType;
        ctx.Response.Headers["Cache-Control"] = "private, max-age=600";
        ctx.Response.ContentLength64 = icon.Bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(icon.Bytes);
        ctx.Response.Close();
    }

    private static bool IconAllowed(CehoConfig cfg, string path)
    {
        var how = Os.IsLinux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (cfg.Apps.Any(a => a.Folder.Equals(path, how))) return true;
        try
        {
            var catalog = InstalledAppCatalog.Detect(cfg.Language);
            return catalog.Any(e => e.Path.Equals(path, how))
                   || cfg.Apps.Any(a => AppIcons.Source(a, catalog).Equals(path, how));
        }
        catch { return false; }
    }

    private static void AppIcon(StringBuilder sb, string path, string label)
    {
        sb.Append("<span class=ico data-letter=\"").Append(E(Initial(label)))
          .Append("\"><img loading=lazy alt=\"\" src=\"/icon?path=")
          .Append(E(Uri.EscapeDataString(path))).Append("\" onerror=\"this.remove()\"></span>");
    }

    internal static string Initial(string label)
    {
        var text = label.Trim();
        return text.Length == 0 ? "?" : StringInfo.GetNextTextElement(text, 0).ToUpperInvariant();
    }

    private static void RenderInstalledPicker(StringBuilder sb, Func<string, object[], string> S,
        IReadOnlyList<InstalledAppCatalog.Entry> installed, string tab, string? wizardStep)
    {
        if (installed.Count == 0)
        {
            sb.Append("<p class=empty>").Append(E(S("apps_installed_all_added", []))).Append("</p>");
            return;
        }

        sb.Append("<form class=app-pick method=post action=/apps/installed>")
          .Append("<input type=hidden name=tab value=").Append(tab).Append('>');
        if (wizardStep is not null)
            sb.Append("<input type=hidden name=wizard value=").Append(wizardStep).Append('>');

        var id = "app-filter-" + tab;
        sb.Append("<label class=sr-only for=").Append(id).Append('>')
          .Append(E(S("apps_pick_search", []))).Append("</label>")
          .Append("<input class=app-filter type=search autocomplete=off id=").Append(id)
          .Append(" placeholder=\"").Append(E(S("apps_pick_search", []))).Append("\">");

        sb.Append("<div class=app-groups>");
        var groups = installed.GroupBy(InstalledAppCatalog.GroupOf).OrderBy(g => g.Key).ToList();
        foreach (var group in groups)
        {
            sb.Append("<div class=app-group>");
            if (groups.Count > 1)
                sb.Append("<h4 class=app-group-title>").Append(E(S("apps_group_" + group.Key.ToString().ToLowerInvariant(), []))).Append("</h4>");
            sb.Append("<div class=app-grid>");
            foreach (var app in group)
            {
                sb.Append("<button class=app-card type=submit name=path value=\"").Append(E(app.Path))
                  .Append("\" data-name=\"").Append(E(app.Name.ToLowerInvariant())).Append("\">");
                AppIcon(sb, app.Path, app.Name);
                sb.Append("<span class=app-name title=\"").Append(E(app.Path)).Append("\">")
                  .Append(E(app.Name)).Append("</span></button>");
            }
            sb.Append("</div></div>");
        }
        sb.Append("</div></form>");
    }

    private static void AppendProductLogo(StringBuilder sb)
    {
        sb.Append("<span class=product-logo><img class=\"logo logo-light\" src=\"")
          .Append(ProductBrand.LogoDataUri).Append("\" alt=\"CehoProxy\"><img class=\"logo logo-dark\" src=\"")
          .Append(ProductBrand.DarkLogoDataUri).Append("\" alt=\"CehoProxy\"></span>");
    }

    private static string RenderGate(CehoConfig cfg, string? error)
    {
        var sb = new StringBuilder();
        Head(sb, cfg, null);
        sb.Append("<div class=gate>");
        AppendProductLogo(sb);
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

        sb.Append("<title>CehoProxy</title><link rel=icon type=\"image/svg+xml\" href=\"").Append(ProductBrand.IconDataUri).Append("\">").Append(WebUi.ThemeEarlyScript).Append("<style>").Append(WebUi.Css).Append(TunnelUi.Css).Append("</style></head>");

        // Пока идёт операция, страница перерисовывается каждые пару секунд.
        // Появление разделов на таких перерисовках только мельтешит, поэтому его выключаем.
        sb.Append(job is { Running: true } ? "<body class=busy>" : "<body>");
    }

    private string RenderPage(
        CehoConfig cfg, ControlState st, string tab, string? flash, bool flashErr, Job? job,
        LogView logView = LogView.All, string? tunnelFolder = null, string? wizard = null, string? pickedPath = null)
    {
        string S(string key, params object[] a) => Strings.T(cfg.Language, key, cfg.SimplePanel, a);
        var sb = new StringBuilder();
        Head(sb, cfg, job);
        sb.Append("<div class=wrap>");

        sb.Append("<header>");
        AppendProductLogo(sb);
        sb.Append("<span class=mark>Ceho<span>Proxy</span></span>");
        sb.Append("<form class=mode method=post action=/mode aria-label=\"").Append(E(S("mode_label")))
          .Append("\"><input type=hidden name=tab value=\"").Append(E(tab)).Append("\">");
        sb.Append("<span").Append(cfg.SimplePanel ? " class=selected" : "").Append('>')
          .Append(E(S("mode_simple"))).Append("</span><button type=submit class=mode-toggle role=switch name=mode value=")
          .Append(cfg.SimplePanel ? CehoConfig.PanelModePro : CehoConfig.PanelModeSimple)
          .Append(" aria-checked=").Append(cfg.SimplePanel ? "false" : "true")
          .Append(" aria-label=\"").Append(E(S("mode_pro"))).Append("\"></button><span")
          .Append(!cfg.SimplePanel ? " class=selected" : "").Append('>').Append(E(S("mode_pro"))).Append("</span>");
        sb.Append("</form>");
        sb.Append("<button type=button id=theme class=theme data-light=\"").Append(E(S("theme_to_light")))
          .Append("\" data-dark=\"").Append(E(S("theme_to_dark"))).Append("\">")
          .Append(WebUi.ThemeIcons).Append("</button>");
        sb.Append(WebUi.ThemeScript);
        sb.Append("</header>");

        var tabs = cfg.SimplePanel
            ? new (string Id, string Key)[] { ("state", "nav_state"), ("apps", "nav_apps"), ("subs", "nav_subs"), ("help", "nav_help") }
            : new (string Id, string Key)[]
            {
                ("state", "nav_state"), ("apps", "nav_apps"), ("sites", "nav_sites"), ("subs", "nav_subs"),
                ("exit", "nav_exit"), ("doctor", "nav_doctor"), ("access", "nav_access"),
            };
        var more = cfg.SimplePanel
            ? Array.Empty<(string Id, string Key)>()
            : new (string Id, string Key)[] { ("browser", "nav_browser"), ("log", "nav_log"), ("help", "nav_help") };
        void Tab(string id, string key) =>
            sb.Append("<a href=\"/?tab=").Append(id).Append('"')
              .Append(id == tab ? " class=on" : "").Append('>').Append(E(S(key))).Append("</a>");
        sb.Append("<nav class=tabs>");
        foreach (var (id, key) in tabs) Tab(id, key);
        var inMore = more.Any(m => m.Id == tab);
        if (more.Length > 0)
        {
            sb.Append("<details class=more><summary").Append(inMore ? " class=on" : "").Append('>')
              .Append(E(inMore ? S(more.First(m => m.Id == tab).Key) : S("nav_more"))).Append("</summary><div>");
            foreach (var (id, key) in more) Tab(id, key);
            sb.Append("</div></details>");
        }
        sb.Append("</nav>");
        if (tab is "state" or "doctor" or "apps")
            sb.Append("<div class=freshness><span id=freshness-text role=status aria-live=polite></span><button id=refresh-retry type=button class=ghost hidden>")
              .Append(E(cfg.Language == "ru" ? "Повторить" : "Retry")).Append("</button></div>");

        if (!string.IsNullOrEmpty(flash))
            sb.Append("<div class=\"flash ").Append(flashErr ? "err" : "notice").Append("\">")
              .Append(E(flash)).Append("</div>");

        RenderJob(sb, cfg, job, tab, S);
        RenderPending(sb, tab, job, S);

        switch (tab)
        {
            case "apps": RenderApps(sb, cfg, S, tunnelFolder, pickedPath); break;
            case "sites": RenderSites(sb, cfg, S); break;
            case "subs": RenderSubs(sb, cfg, S); break;
            case "exit": RenderExit(sb, cfg, S); break;
            case "browser": RenderBrowser(sb, cfg, S); break;
            case "doctor": RenderDoctor(sb, cfg, S); break;
            case "log": RenderLog(sb, cfg, logView, S); break;
            case "access": RenderAccess(sb, cfg, S); break;
            case "help": RenderHelp(sb, cfg, S); break;
            default: RenderState(sb, cfg, st, S, wizard, pickedPath); break;
        }

        if (tab == "access" || tab == "help")
            SafetyPanel.RenderRecovery(sb, cfg, Root, tab);
        if (tab == "state") RenderReconnectHistory(sb, cfg);

        sb.Append("<footer><a class=forged href=\"").Append(Brand.Site).Append("\" target=_blank rel=noopener>")
          .Append("<img src=\"").Append(Brand.LogoDataUri).Append("\" alt=\"\">")
          .Append(E(S("forged"))).Append("</a>")
          .Append("<span class=foot-links><a href=\"").Append(Brand.Telegram)
          .Append("\" target=_blank rel=noopener>").Append(E(S("telegram"))).Append("</a>")
          .Append("<a href=\"").Append(E(Brand.RepoUrl(cfg.UpdateRepo)))
          .Append("\" target=_blank rel=noopener>").Append(E(S("product_page"))).Append("</a>")
          .Append("<a href=\"https://db-ip.com\" target=_blank rel=noopener>")
          .Append(E(S("geoip_attribution"))).Append("</a>")
          .Append("<a href=\"https://creativecommons.org/licenses/by/4.0/\" target=_blank rel=noopener>CC BY 4.0</a>")
          .Append("<span>").Append(E(S("footer_local"))).Append("</span></span></footer>");
        sb.Append("</div>");

        sb.Append(WebUi.InteractionScript).Append(WebUi.ToastScript);
        if (job is { Running: true }) sb.Append(WebUi.JobScript);
        if (tab == "subs" && cfg.Subscriptions.Count > 0) sb.Append(WebUi.SubModalScript);
        if (tab is "apps" or "state") sb.Append(WebUi.AppFilterScript);
        if (tab == "access") sb.Append(WebUi.TransferPartsScript);
        if (tab is "state" or "doctor" or "apps") sb.Append(WebUi.StateRefreshScript);
        if (tab is "state" or "apps") sb.Append(TunnelUi.Script);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void RenderReconnectHistory(StringBuilder sb, CehoConfig cfg)
    {
        var history = ReconnectHistory.Shared.Snapshot();
        if (history.Recent.Count == 0) { sb.Append("<section data-live=reconnect></section>"); return; }
        string T(string ru, string en) => cfg.Language == "ru" ? ru : en;
        sb.Append("<section data-live=reconnect><details class=route-details><summary>")
          .Append(E(T("Восстановление соединения", "Connection recovery"))).Append("</summary>");
        foreach (var item in history.Recent.Take(5))
        {
            sb.Append("<div class=consequences><b>").Append(E(item.Reason)).Append("</b><br>")
              .Append(E(T("Попытка ", "Attempt "))).Append(item.Attempt);
            if (item.MaxAttempts is { } max) sb.Append(" / ").Append(max);
            sb.Append(" · ").Append(E(item.State switch { "running" => T("выполняется", "in progress"), "succeeded" => T("восстановлено", "recovered"), _ => T("не удалось", "failed") }))
              .Append("<br>").Append(E(item.State == "running" ? item.Stage : item.Result)).Append("<br><span class=hint>")
              .Append(E(item.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"))).Append("</span></div>");
        }
        sb.Append("<p class=hint>").Append(E(T("История текущего запуска службы; показано до 5 последних событий.", "History from this service session; showing up to 5 recent events.")))
          .Append("</p></details></section>");
    }

    private void RenderPending(StringBuilder sb, string tab, Job? job, Func<string, object[], string> S)
    {
        var count = Volatile.Read(ref _pending);
        sb.Append("<div data-live=pending-actions>");
        if (count == 0 || job is { Running: true } || !_state().Running) { sb.Append("</div>"); return; }
        sb.Append("<form class=pending method=post action=/apply><input type=hidden name=confirm_apply value=1><input type=hidden name=tab value=\"")
          .Append(E(tab)).Append("\"><span><b>").Append(E(S("pending_title", new object[] { count })))
          .Append("</b> ").Append(E(S("pending_hint", []))).Append("</span><button>")
          .Append(E(S("btn_apply_pending", []))).Append("</button></form>");
        sb.Append("<p class=consequences>").Append(E(ConfirmApplyMessage(CehoConfig.Load(_configPath)))).Append("</p>");
        sb.Append("<div class=toast id=toast data-count=").Append(count).Append(" role=status><span>")
          .Append(E(S("pending_toast", []))).Append("</span><form method=post action=/apply><input type=hidden name=confirm_apply value=1>")
          .Append("<input type=hidden name=tab value=\"").Append(E(tab)).Append("\"><button>")
          .Append(E(S("btn_apply_pending", []))).Append("</button></form>")
          .Append("<button type=button class=ghost id=toast-x aria-label=\"").Append(E(S("btn_close", [])))
          .Append("\">×</button></div>").Append(WebUi.ToastScript).Append("</div>");
    }

    /// <summary>Полоса и этап: видно, что операция идёт и на чём именно она стоит.</summary>
    private static void RenderJob(
        StringBuilder sb, CehoConfig cfg, Job? job, string tab, Func<string, object[], string> S)
    {
        if (job is null) return;
        var snapshot = job.Snapshot();

        var cls = job.State switch
        {
            JobState.Running => "run",
            JobState.Done => "ok",
            _ => "err",
        };

        sb.Append("<div class=\"job ").Append(cls).Append("\" id=jp data-job=\"").Append(E(job.Id)).Append("\"")
          .Append(job.RelaunchPanel ? " data-relaunch=1" : "")
          .Append(" data-wait=\"").Append(E(S("job_wait_panel", []))).Append("\"")
          .Append(" data-elapsed=\"").Append(snapshot.Seconds.ToString("F1", CultureInfo.InvariantCulture)).Append("\"")
          .Append(" data-run=\"").Append(E(S("job_running", ["{0}"]))).Append("\">");
        sb.Append("<ol class=startup-steps id=startup-steps").Append(snapshot.StartupStep == 0 ? " hidden" : "").Append('>');
        var stepLabels = cfg.Language == "ru" ? new[] { "Подготовка", "Подключение", "Проверка" }
            : new[] { "Prepare", "Connect", "Check" };
        for (var i = 1; i <= 3; i++)
            sb.Append("<li data-step=").Append(i).Append(" class=\"")
              .Append(i < snapshot.StartupStep || snapshot.State == JobState.Done ? "complete" : i == snapshot.StartupStep ? "current" : "")
              .Append("\"").Append(i == snapshot.StartupStep && snapshot.State == JobState.Running ? " aria-current=step" : "")
              .Append("><span class=step-number>").Append(i).Append("</span>").Append(E(stepLabels[i - 1])).Append("</li>");
        sb.Append("</ol>");
        var metric = snapshot.Indeterminate
            ? snapshot.State switch
            {
                JobState.Running => cfg.Language == "ru" ? "Выполняется" : "In progress",
                JobState.Failed => cfg.Language == "ru" ? "Не удалось" : "Failed",
                _ => cfg.Language == "ru" ? "Готово" : "Done",
            }
            : snapshot.Percent + "%";
        sb.Append("<div class=job-head><b>").Append(E(job.Title)).Append("</b>")
          .Append("<span class=job-num id=jn>").Append(E(metric)).Append("</span></div>");
        sb.Append("<div class=\"bar").Append(snapshot.Indeterminate && snapshot.State == JobState.Running ? " indeterminate" : "").Append("\" role=progressbar aria-label=\"").Append(E(job.Title)).Append("\" aria-valuemin=0 aria-valuemax=100><span id=jf style=\"transform:scaleX(")
          .Append((job.Percent / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(")\"></span></div>");
        sb.Append("<div class=job-stage id=js>").Append(E(job.Stage)).Append("</div>");
        sb.Append("<div class=job-foot><span id=jt>")
          .Append(E(S(job.Running ? "job_running" : job.IsError ? "job_failed" : "job_done",
              new object[] { job.Elapsed.TotalSeconds.ToString("F1") }))).Append("</span>");

        if (!job.Running)
            sb.Append(" · <a href=\"/?tab=").Append(E(tab)).Append("\">").Append(E(S("job_hide", [])))
              .Append("</a>");
        sb.Append("</div>");

        if (job.Running)
            sb.Append("<div class=job-status id=job-status role=status aria-live=polite></div><button type=button id=job-retry class=ghost hidden>")
              .Append(E(cfg.Language == "ru" ? "Проверить связь" : "Check connection")).Append("</button>");
        sb.Append("<p class=\"hint job-safety\">").Append(E(cfg.Language == "ru"
            ? "Закрытие страницы не отменяет действие. Безопасная отмена этого этапа не поддерживается."
            : "Closing this page does not cancel the operation. Safe cancellation of this phase is not supported.")).Append("</p>");
        if (job.Kind == JobPower && job.OperationIdentity is "start" or "restart")
        {
            sb.Append("<form method=post action=\"/control/").Append(job.OperationIdentity)
              .Append("\" id=job-retry-operation").Append(job.State == JobState.Failed ? "" : " hidden")
              .Append("><input type=hidden name=tab value=\"").Append(E(tab)).Append("\"><button>")
              .Append(E(cfg.Language == "ru" ? "Повторить запуск" : "Retry start")).Append("</button></form>");
        }
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

    private void RenderState(StringBuilder sb, CehoConfig cfg, ControlState st, Func<string, object[], string> S,
        string? wizard = null, string? pickedPath = null)
    {
        string T(string ru, string en) => AppObservation.Text(cfg, ru, en);
        var step = !cfg.Subscriptions.Any(s => s.Enabled) ? 1 : !cfg.Apps.Any(a => a.Enabled) ? 2 : wizard == "3" ? 3 : 0;
        var guarded = LeakGuard.IsActive(Root);
        var power = Jobs.Active(JobPower) ?? Jobs.Active(JobApply) ?? Jobs.Active(JobRestore);
        var live = st.Running ? AppsLive() : null;
        var now = DateTime.UtcNow;
        var observations = cfg.Apps.Where(a => a.Enabled).Select(a => (App: a, Observation: AppObservation.Evaluate(
            cfg, a, live?.FirstOrDefault(l => l.Folder == a.Folder), st.Running, guarded, _appsLiveAtUtc, now, _appsLiveFailed, AppRulesPending))).ToList();
        var leaks = observations.Where(a => a.Observation.Leak).ToList();
        var verified = observations.Count(a => a.Observation.Verified);
        var (cls, title, detail) =
            leaks.Count > 0 ? ("bad", leaks.Any(a => a.Observation.Kind == "leak")
                ? T("Обнаружен прямой трафик", "Direct traffic detected")
                : T("Обнаружен неожиданный трафик", "Unexpected app traffic detected"),
                T("Проверьте программы: ", "Check these apps: ") + string.Join(", ", leaks.Select(a => a.App.Label)))
            : power is not null ? ("wait", power.Title, S("hero_busy", []))
            : st.Running && st.ExitIp is not null ? ("on", T("Туннель подключён", "Tunnel connected"),
                T("Проверочный выход туннеля: ", "Tunnel probe exit: ") + (st.ExitCountry ?? "?") + " · " + st.ExitIp)
            : st.Running && st.Probed ? ("bad", S("hero_no_exit", []), S("hero_no_exit_detail", []))
            : st.Running ? ("wait", S("hero_checking", []), S("state_checking", []))
            : DaemonControl.IsRecovering(Root) ? ("wait", S("hero_recovering", []), S("hero_recovering_detail", []))
            : DaemonControl.IsStarting(Root, TimeSpan.Zero) ? ("wait", S("hero_checking", []), S("state_starting", []))
            : guarded ? ("warn", T("Туннель выключен", "Tunnel is off"), T("Защитный барьер отмечен как включённый", "Guard is recorded as enabled"))
            : ("off", T("Туннель выключен", "Tunnel is off"), T("Защитный барьер не подтверждён", "Guard is not confirmed"));

        sb.Append("<section data-live=hero><div class=\"hero ").Append(cls).Append("\" role=status aria-live=polite><span class=dot></span><div><h1>")
          .Append(E(title)).Append("</h1><p>").Append(E(detail)).Append("</p><p class=scope-note>")
          .Append(E(T("VPN предназначен только для выбранных программ. Их фактические соединения проверяются отдельно.",
              "VPN is configured for selected apps only. Their actual connections are checked separately."))).Append("</p></div>")
          .Append("<form method=post><input type=hidden name=tab value=state>");
        if (power is not null) { }
        else if (st.Running || DaemonControl.IsRecovering(Root))
        {
            sb.Append("<button type=submit formaction=\"/control/stop\" class=danger>").Append(E(S("btn_off", []))).Append("</button>");
            if (!cfg.SimplePanel && st.Running)
                sb.Append("<button type=submit formaction=\"/control/restart\" class=ghost>").Append(E(S("btn_restart", []))).Append("</button>");
        }
        else
            sb.Append("<button type=submit formaction=\"/control/start\" class=big").Append(Os.IsElevated() ? "" : " disabled").Append('>')
              .Append(E(S("btn_on", []))).Append("</button>");
        sb.Append("</form></div><details class=consequences><summary>")
          .Append(E(T("Что произойдёт при выключении?", "What happens when I turn it off?")))
          .Append("</summary><p>").Append(E(AppObservation.StopConsequence(cfg, Os.IsWindows, guarded))).Append("</p></details>");
        if (observations.Count > 0)
        {
            var summaryClass = leaks.Count > 0 ? "bad" : verified == observations.Count && st.Running ? "on" : "warn";
            sb.Append("<div class=\"protection-summary ").Append(summaryClass).Append("\"><div><b>")
              .Append(E(leaks.Count > 0
                  ? T($"Требуют внимания: {leaks.Count}", $"Needs attention: {leaks.Count}")
                  : T($"VPN-трафик замечен у {verified} из {observations.Count} программ", $"VPN traffic observed for {verified} of {observations.Count} apps")))
              .Append("</b><p class=hint>")
              .Append(E(T("Отсутствие трафика не подтверждает защиту. Статус относится только к последней выборке соединений.",
                  "No traffic does not verify protection. Status describes only the latest connection sample.")))
              .Append("</p></div><a class=ghost href=\"/?tab=doctor\">")
              .Append(E(T("Проверить программы", "Check apps"))).Append("</a></div>");
        }
        sb.Append("</section>");
        RenderLiveApps(sb, cfg, st, guarded, S);

        sb.Append("<section>");
        if (power is null && st.Running && st.Probed && st.ExitIp is null)
            sb.Append("<div class=\"flash err\"><b>").Append(E(S("no_exit_todo_title", []))).Append("</b> ")
              .Append(E(S("no_exit_todo", []))).Append("<br><a href=\"/?tab=doctor\"><b>")
              .Append(E(S("no_exit_open_check", []))).Append("</b></a></div>");
        if (!Auth.HasPassword(cfg))
            sb.Append("<div class=\"flash warn\">").Append(E(S("auth_no_password", [])))
              .Append(" <a href=\"").Append(cfg.SimplePanel ? "#settings" : "/?tab=access").Append("\">")
              .Append(E(S("auth_set_link", []))).Append("</a></div>");

        var checks = (PanelPreflight?.Invoke(cfg) ?? Preflight.Run(cfg, Root, cfg.SimplePanel));
        var problems = checks.Where(c => c.Level != Preflight.Level.Ok)
            .Where(c => !c.Title.Contains("орт ", StringComparison.OrdinalIgnoreCase)
                     && !c.Title.Contains("ort ", StringComparison.OrdinalIgnoreCase))
            .Where(c => step == 0 || (c.Title != S("pf_no_subs", []) && c.Title != S("pf_no_apps", [])))
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
            if (engineMissing && c.Repair == Repair.Engine)
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

        sb.Append("</section>");

        foreach (var broken in cfg.Apps.Where(a => a.Enabled && AppDetector.CoversSystemFolder(a)))
            sb.Append("<section><div class=\"flash err\"><b>").Append(E(S("doc_app_system_folder", [broken.Label])))
              .Append("</b>").Append(E(S("doc_app_system_folder_detail", [broken.Folder])))
              .Append("<form method=post action=/apps/remove><input type=hidden name=tab value=apps>")
              .Append("<input type=hidden name=folder value=\"").Append(E(broken.Folder)).Append("\">")
              .Append("<button class=danger>").Append(E(S("fix_system_folder", []))).Append("</button></form></div></section>");

        if (step > 0) RenderWizard(sb, cfg, st, step, S);
        if (step is 0 or 3)
        {
            RenderCheckSummary(sb, cfg, S);
        }

        if (!cfg.SimplePanel)
        {
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
        }


        var auto = Autostart.IsEnabled();
        sb.Append("<details class=settings><summary>").Append(E(S("service_title", []))).Append("</summary><section><div class=lines>");
        var engineHere = Os.ResolveSingBox(Root) is not null;
        sb.Append("<div class=line><span>").Append(E(S("upd_current", new object[] { Updater.CurrentVersion }))).Append("</span>")
          .Append("<form method=post action=/update><input type=hidden name=tab value=state>")
          .Append("<button class=ghost>").Append(E(S("upd_check", []))).Append("</button></form>")
          .Append("<form method=post action=/update><input type=hidden name=tab value=state><input type=hidden name=install value=1>")
          .Append("<button class=ghost>").Append(E(S("upd_apply", []))).Append("</button></form></div>");
        sb.Append("<div class=\"line ").Append(cfg.AutoUpdate ? "on" : "off").Append("\"><span><span class=dot></span> ")
          .Append(E(cfg.AutoUpdate ? S("upd_auto_on", []) : S("upd_auto_off", []))).Append("</span>")
          .Append("<form method=post action=/autoupdate><input type=hidden name=tab value=state>")
          .Append(cfg.AutoUpdate ? "" : "<input type=hidden name=enable value=1>")
          .Append("<button class=ghost>").Append(E(cfg.AutoUpdate ? S("upd_auto_del", []) : S("upd_auto_add", [])))
          .Append("</button></form></div>");
        if (cfg.AutoUpdatedVersion == Updater.CurrentVersion && cfg.AutoUpdatedAtUtc is { } since)
            sb.Append("<div class=line><span>")
              .Append(E(S("upd_auto_done", new object[]
              {
                  cfg.AutoUpdatedVersion, since.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
              })))
              .Append("</span></div>");
        if (engineHere)
        {
            var engineVersion = EngineVersion();
            sb.Append("<div class=line><span>")
              .Append(E(engineVersion is null ? S("engine_line_unknown", [])
                  : Installer.EngineOutdated(engineVersion) ? S("engine_line_old", [engineVersion, Installer.EngineVersion])
                  : S("engine_line", [engineVersion])))
              .Append("</span>");
            if (Installer.EngineOutdated(engineVersion))
                sb.Append("<form method=post action=/engine/update><input type=hidden name=tab value=state>")
                  .Append("<button class=ghost>").Append(E(S("engine_update_btn", []))).Append("</button></form>");
            sb.Append("</div>");
            sb.Append("<div class=\"line ").Append(cfg.EngineAutoUpdate ? "on" : "off").Append("\"><span><span class=dot></span> ")
              .Append(E(cfg.EngineAutoUpdate ? S("engine_auto_on", []) : S("engine_auto_off", []))).Append("</span>")
              .Append("<form method=post action=/engine/autoupdate><input type=hidden name=tab value=state>")
              .Append(cfg.EngineAutoUpdate ? "" : "<input type=hidden name=enable value=1>")
              .Append("<button class=ghost>").Append(E(cfg.EngineAutoUpdate ? S("engine_auto_del", []) : S("engine_auto_add", [])))
              .Append("</button></form></div>");
        }
        sb.Append("<div class=\"line ").Append(auto ? "on" : "off").Append("\"><span><span class=dot></span> ")
          .Append(E(auto ? S("autostart_on", []) : S("autostart_off", []))).Append("</span>")
          .Append("<form method=post action=/autostart><input type=hidden name=tab value=state>")
          .Append(auto ? "" : "<input type=hidden name=enable value=1>")
          .Append("<button class=ghost>").Append(E(auto ? S("autostart_del", []) : S("autostart_add", [])))
          .Append("</button></form></div>");
        sb.Append("<div class=\"line ").Append(cfg.TrayControls ? "on" : "off").Append("\"><span><span class=dot></span> ")
          .Append(E(cfg.TrayControls ? S("tray_controls_on", []) : S("tray_controls_off", []))).Append("</span>")
          .Append("<form method=post action=/tray-controls><input type=hidden name=tab value=state>")
          .Append(cfg.TrayControls ? "" : "<input type=hidden name=enable value=1>")
          .Append("<button class=ghost>").Append(E(cfg.TrayControls ? S("tray_controls_del", []) : S("tray_controls_add", [])))
          .Append("</button></form></div>");
        if (cfg.TrayControls && !Auth.HasPassword(cfg))
            sb.Append("<div class=line><span>").Append(E(S("tray_controls_open", []))).Append("</span></div>");
        sb.Append("</div></section></details>");

        if (cfg.SimplePanel)
        {
            sb.Append("<details class=settings id=settings><summary>").Append(E(S("settings_title", []))).Append("</summary>");
            RenderAccess(sb, cfg, S, "state");
            sb.Append("</details>");
        }
    }

    private void RenderWizard(StringBuilder sb, CehoConfig cfg, ControlState st, int step, Func<string, object[], string> S)
    {
        string T(string ru, string en) => AppObservation.Text(cfg, ru, en);
        sb.Append("<section class=wizard data-live=wizard><ol class=wizard-steps>");
        for (var i = 1; i <= 3; i++)
            sb.Append("<li").Append(i == step ? " class=now aria-current=step" : i < step ? " class=done" : "").Append('>')
              .Append(E(i == 3 ? T("Проверить соединение", "Verify connection") : S($"wiz_step{i}", []))).Append("</li>");
        sb.Append("</ol><h2>").Append(E(step == 3 ? T("Проверим реальное соединение", "Verify a real connection") : S($"wiz_title{step}", [])))
          .Append("</h2><p class=lede>")
          .Append(E(step == 3 ? T("Подписка, туннель и трафик программы проверяются отдельно.", "The subscription, tunnel and app traffic are checked separately.") : S($"wiz_hint{step}", []))).Append("</p>");

        if (step == 1)
        {
            sb.Append("<form class=row method=post action=/subs/add><input type=hidden name=tab value=state>")
              .Append("<input type=hidden name=kind value=sub><input type=hidden name=wizard value=2>")
              .Append("<label class=sr-only for=wiz-url>").Append(E(S("wiz_title1", []))).Append("</label>")
              .Append("<input id=wiz-url type=url name=url required placeholder=\"https://…\">")
              .Append("<button>").Append(E(S("wiz_next", []))).Append("</button></form>");
            if (cfg.Subscriptions.Count > 0)
                sb.Append("<p><a href=\"/?tab=subs\">").Append(E(T("Включить существующую подписку", "Enable an existing subscription"))).Append("</a></p>");
            sb.Append("<p class=hint>").Append(E(S("wiz_no_link", [])))
              .Append(" <a href=\"/?tab=help\">").Append(E(S("sub_where_title", []))).Append("</a></p>");
        }
        else if (step == 2)
        {
            List<InstalledAppCatalog.Entry> installed;
            try { installed = InstalledAppCatalog.Detect(cfg.Language).Where(e => !AppCoverage.IsEntryCovered(cfg, e.Path)).ToList(); }
            catch { installed = new(); }
            if (installed.Count > 0)
            {
                RenderInstalledPicker(sb, S, installed, "state", "3");
            }
            sb.Append("<p class=hint>").Append(E(S("wiz_other_app", []))).Append("</p>")
              .Append("<form class=row method=post action=/apps/add><input type=hidden name=tab value=state>")
              .Append("<input type=hidden name=wizard value=3>")
              .Append("<label class=sr-only for=wiz-path>").Append(E(S("wiz_other_app", []))).Append("</label>")
              .Append("<input id=wiz-path type=text name=path required placeholder=\"")
              .Append(E(S(Os.Kind switch
              {
                  OsKind.Windows => "apps_placeholder_win",
                  OsKind.Mac => "apps_placeholder_mac",
                  _ => "apps_placeholder_linux",
              }, []))).Append("\">")
              .Append("<button class=ghost>").Append(E(S("btn_add", []))).Append("</button></form>");
        }
        else
        {
            var now = DateTime.UtcNow;
            var subscriptionOk = cfg.Subscriptions.Any(s => AppObservation.SubscriptionVerified(s, now));
            var subscriptionFailed = !subscriptionOk && cfg.Subscriptions.Any(s => s.Enabled && (s.LastCheckOk == false || s.ExpiresUtc <= now));
            var tunnelOk = st.Running && st.Probed && st.ExitIp is not null;
            var live = st.Running ? AppsLive() : null;
            now = DateTime.UtcNow;
            var apps = cfg.Apps.Where(a => a.Enabled).ToList();
            var results = apps.Select(a => AppObservation.Evaluate(cfg, a, live?.FirstOrDefault(l => l.Folder == a.Folder),
                st.Running, LeakGuard.IsActive(Root), _appsLiveAtUtc, now, _appsLiveFailed, AppRulesPending)).ToList();
            var appsOk = results.Count > 0 && results.All(r => r.Verified);
            var failed = subscriptionFailed || (st.Running && st.Probed && st.ExitIp is null) || results.Any(r => r.Leak);
            var complete = subscriptionOk && tunnelOk && appsOk;
            sb.Append("<ol class=verification-checklist>");
            void Check(bool ok, string label, string note)
            {
                sb.Append("<li class=").Append(ok ? "on" : "off").Append("><b>")
                  .Append(E((ok ? "✓ " : "○ ") + label)).Append("</b><span class=hint>").Append(E(note)).Append("</span></li>");
            }
            Check(subscriptionOk, T("Подписка", "Subscription"), subscriptionOk
                ? T("Есть доступные серверы; проверка не старше суток", "Available servers; checked within the last day")
                : T("Проверьте подписку и доступность серверов", "Check the subscription and server availability"));
            Check(tunnelOk, T("Туннель", "Tunnel"), tunnelOk
                ? T("Проверочный выход получен", "Probe exit received")
                : T("Включите туннель и дождитесь проверочного выхода", "Start the tunnel and wait for a probe exit"));
            Check(appsOk, T("Трафик программ", "App traffic"), appsOk
                ? T("В свежей выборке замечен VPN-трафик без утечек", "Fresh sample contains VPN traffic without observed leaks")
                : T("Откройте выбранные программы, создайте трафик и перепроверьте", "Open selected apps, generate traffic and recheck"));
            sb.Append("</ol><div class=\"wizard-result ").Append(complete ? "on" : failed ? "bad" : "wait").Append("\" role=status><b>")
              .Append(E(complete ? T("Соединение проверено", "Connection verified")
                  : failed ? T("Проверка требует внимания", "The check needs attention")
                  : T("Ожидаем проверку соединения", "Waiting for connection verification"))).Append("</b></div>");
            if (!subscriptionOk)
                sb.Append("<form class=row method=post action=/subs/check><input type=hidden name=tab value=state><input type=hidden name=wizard value=3><button class=ghost>")
                  .Append(E(T("Проверить подписку", "Check subscription"))).Append("</button></form>");
            if (!st.Running)
                sb.Append("<form class=row method=post action=/control/start><input type=hidden name=tab value=state><input type=hidden name=wizard value=3><button")
                  .Append(Os.IsElevated() ? "" : " disabled").Append('>').Append(E(S("btn_on", []))).Append("</button></form>");
            foreach (var app in apps)
                AppendAppRecheck(sb, cfg, app, "state", "3");
            if (failed)
                sb.Append("<p><a href=\"/?tab=doctor\">").Append(E(T("Открыть диагностику", "Open diagnostics"))).Append("</a></p>");
            if (complete)
                sb.Append("<p><a class=button href=\"/?tab=state\">").Append(E(S("wiz_done", []))).Append("</a></p>");
        }

        sb.Append("</section>");
    }

    public sealed record AppLive(string Folder, int Processes, int Tunneled, int Direct, int EngineVpn = 0, int EngineDirect = 0);

    public Func<IReadOnlyList<AppLive>>? OnAppsLive { get; set; }

    private IReadOnlyList<AppLive>? _appsLive;
    private DateTime _appsLiveAtUtc;
    private DateTime _appsLiveAttemptAtUtc;
    private DateTime _appsLiveConfigAtUtc;
    private bool _appsLiveFailed;
    private readonly object _appsLiveSync = new();
    private int _observationGeneration;
    // A read-only diagnostics seam for deterministic panel fixtures; normal panels always use Preflight.Run.
    internal Func<CehoConfig, IReadOnlyList<Preflight.Check>>? PanelPreflight { get; set; }
    private bool AppRulesPending => Volatile.Read(ref _pending) > 0 || Jobs.Active(JobApply) is not null
        || Jobs.Active(JobPower) is not null || Jobs.Active(JobRestore) is not null;
    private string? _engineVersion;
    private DateTime _engineVersionAtUtc;

    private string? EngineVersion()
    {
        if (_engineVersion is null || DateTime.UtcNow - _engineVersionAtUtc > TimeSpan.FromMinutes(10))
        {
            _engineVersion = Os.ResolveSingBox(Root) is { } engine ? Installer.EngineVersionOf(engine) : null;
            _engineVersionAtUtc = DateTime.UtcNow;
        }
        return _engineVersion;
    }

    private void ForgetEngineVersion() => _engineVersion = null;

    private void InvalidateAppObservations()
    {
        lock (_appsLiveSync)
        {
            Interlocked.Increment(ref _observationGeneration);
            _appsLive = null;
            _appsLiveAtUtc = default;
            _appsLiveAttemptAtUtc = default;
            _appsLiveFailed = false;
        }
    }

    private IReadOnlyList<AppLive>? AppsLive()
    {
        if (OnAppsLive is null) return null;
        lock (_appsLiveSync)
        {
            var now = DateTime.UtcNow;
            var configAt = File.GetLastWriteTimeUtc(_configPath);
            if (_appsLiveAttemptAtUtc == default || now - _appsLiveAttemptAtUtc > TimeSpan.FromSeconds(10)
                || configAt != _appsLiveConfigAtUtc)
            {
                _appsLiveAttemptAtUtc = now;
                _appsLiveConfigAtUtc = configAt;
                try
                {
                    _appsLive = OnAppsLive();
                    _appsLiveAtUtc = DateTime.UtcNow;
                    _appsLiveFailed = false;
                }
                catch (Exception ex)
                {
                    // Never turn a failed refresh into a fresh, green observation.
                    _appsLiveFailed = true;
                    Log.Warn($"панель не смогла проверить программы: {ex.Message}");
                }
            }
            return _appsLive;
        }
    }

    private (string? Message, bool IsError, string? JobId) CheckApp(CehoConfig cfg, AppEntry app)
    {
        var st = _state();
        InvalidateAppObservations();
        var info = st.Running ? AppsLive()?.FirstOrDefault(l => l.Folder == app.Folder) : null;
        var result = AppObservation.Evaluate(cfg, app, info, st.Running, LeakGuard.IsActive(Root),
            _appsLiveAtUtc, DateTime.UtcNow, _appsLiveFailed, AppRulesPending);
        return ($"{app.Label}: {result.Title}. {result.Advice}", result.Leak || result.Kind is "stale" or "unknown", null);
    }

    private void RenderLiveApps(StringBuilder sb, CehoConfig cfg, ControlState st, bool guarded, Func<string, object[], string> S,
        string tab = "state")
    {
        var apps = cfg.Apps.Where(a => a.Enabled).ToList();
        sb.Append("<section data-live=apps id=added-apps><h2>").Append(E(S("apps_title", []))).Append("</h2>");
        if (apps.Count == 0)
        {
            sb.Append("<p class=lede>").Append(E(S("live_no_apps", []))).Append("</p>")
              .Append("<a class=button href=\"/?tab=apps\">").Append(E(S("live_add_app", []))).Append("</a></section>");
            RenderRecommended(sb, cfg, S, tab);
            return;
        }

        var live = st.Running ? AppsLive() : null;
        var catalog = PanelInstalledApps(cfg);
        sb.Append("<ul class=live-apps>");
        foreach (var app in apps)
        {
            var info = live?.FirstOrDefault(l => l.Folder == app.Folder);
            var result = AppObservation.Evaluate(cfg, app, info, st.Running, guarded, _appsLiveAtUtc, DateTime.UtcNow, _appsLiveFailed, AppRulesPending);
            sb.Append("<li id=\"app-").Append(AppIdentity.Id(app)).Append("\" data-app-id=\"").Append(AppIdentity.Id(app))
              .Append("\" data-app-path=\"").Append(E(AppIdentity.PathOf(app))).Append("\" data-rule-state=\"")
              .Append(IsAppRuleApplied(app) ? "applied" : AppRulesPending ? "pending" : st.Running ? "unknown" : "inactive")
              .Append("\" tabindex=-1 class=\"app-observation ").Append(result.Css).Append("\"><div class=app-identity><span class=dot aria-hidden=true></span>");
            AppIcon(sb, AppIcons.Source(app, catalog), app.Label);
            sb.Append("<b>").Append(E(app.Label)).Append("</b></div>");
            RenderAppObservation(sb, cfg, app, info, result, st);
            AppendAppRecheck(sb, cfg, app, tab);
            sb.Append("</li>");
        }
        sb.Append("</ul>")
          .Append("<a href=\"/?tab=apps\">").Append(E(AppObservation.Text(cfg, "Все программы →", "All apps →"))).Append("</a></section>");
    }

    private void RenderAppObservation(StringBuilder sb, CehoConfig cfg, AppEntry app, AppLive? info, AppObservation.Result result,
        ControlState? exit = null, bool withCountry = false)
    {
        string T(string ru, string en) => AppObservation.Text(cfg, ru, en);
        sb.Append("<div class=app-observation-body><span class=\"observation-badge ").Append(result.Css)
          .Append("\" data-observation=\"").Append(result.Kind).Append("\">").Append(E(result.Title))
          .Append("</span>");
        if (result.Verified && exit?.ExitIp is { Length: > 0 } proofIp)
            sb.Append("<p class=app-proof>").Append(E(Strings.T(cfg.Language, "app_proof", exit.ExitCountry ?? "?", proofIp))).Append("</p>");
        if (app.NoInternet || !app.Enabled || app.AllowedNodes.Count > 0)
            sb.Append("<p class=configured-route>").Append(E(T("Настроено: ", "Configured: ") + AppObservation.ConfiguredRoute(cfg, app))).Append("</p>");
        sb.Append("<p class=hint>").Append(E(result.Advice)).Append("</p>");
        if (withCountry) { RenderCountryPicker(sb, cfg, app); RenderNodeHelp(sb, cfg, app); }
        sb.Append("<details class=route-details><summary>").Append(E(T("Подробности", "Details")))
          .Append("</summary><dl class=kv><dt>").Append(E(T("Путь", "Path"))).Append("</dt><dd class=path>").Append(E(app.Folder)).Append("</dd>");
        if (info is not null)
        {
            sb.Append("<dt>").Append(E(T("Совпавших процессов", "Matching processes"))).Append("</dt><dd>").Append(info.Processes).Append("</dd>")
              .Append("<dt>").Append(E(T("Соединения через VPN", "Connections via VPN"))).Append("</dt><dd>").Append(info.EngineVpn).Append("</dd>")
              .Append("<dt>").Append(E(T("Входят в туннель", "Entering the tunnel"))).Append("</dt><dd>").Append(info.Tunneled).Append("</dd>")
              .Append("<dt>").Append(E(T("Вне туннеля", "Outside tunnel"))).Append("</dt><dd>").Append(info.Direct).Append("</dd>")
              .Append("<dt>").Append(E(T("Напрямую по правилам", "Direct by rules"))).Append("</dt><dd>").Append(info.EngineDirect).Append("</dd>");
        }
        sb.Append("</dl></details></div>");
    }

    private void RenderCountryPicker(StringBuilder sb, CehoConfig cfg, AppEntry app)
    {
        if (app.NoInternet || !app.Enabled) return;
        string S(string key) => Strings.T(cfg.Language, key);
        var pool = _pool;
        if (pool is null)
        {
            if (cfg.Subscriptions.Any(x => x.Enabled) && Jobs.Active(JobPool) is null) StartPoolJob(cfg);
            return;
        }
        var groups = pool.Where(n => !n.IsMeta).GroupBy(n => n.CountryCode ?? CountryResolver.Unknown)
            .Where(g => g.Key != CountryResolver.Unknown).OrderByDescending(g => g.Count()).ToList();
        if (groups.Count < 2) return;
        string? current = null;
        var mixed = false;
        if (app.AllowedNodes.Count > 0)
        {
            var codes = pool.Where(n => app.AllowedNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase))
                .Select(n => n.CountryCode ?? CountryResolver.Unknown).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (codes.Count == 1) current = codes[0]; else mixed = true;
        }
        sb.Append("<form class=country-form method=post action=/apps/country><input type=hidden name=tab value=apps>")
          .Append("<input type=hidden name=folder value=\"").Append(E(app.Folder)).Append("\">")
          .Append("<label>").Append(E(S("country_label"))).Append(" <select name=country onchange=\"this.form.submit()\">")
          .Append("<option value=\"\"").Append(current is null && !mixed ? " selected" : "").Append('>').Append(E(S("country_any"))).Append("</option>");
        if (mixed) sb.Append("<option selected disabled>").Append(E(S("country_mixed"))).Append("</option>");
        foreach (var g in groups)
            sb.Append("<option value=\"").Append(E(g.Key)).Append('"').Append(string.Equals(current, g.Key, StringComparison.OrdinalIgnoreCase) ? " selected" : "")
              .Append('>').Append(E(CountryResolver.Flag(g.Key) + " " + (g.First().CountryName ?? g.Key))).Append("</option>");
        sb.Append("</select></label><noscript><button class=ghost>").Append(E(Strings.T(cfg.Language, "btn_save"))).Append("</button></noscript></form>");
    }

    private void RenderNodeHelp(StringBuilder sb, CehoConfig cfg, AppEntry app)
    {
        if (app.NoInternet || !app.Enabled) return;
        string S(string key, params object[] args) => Strings.T(cfg.Language, key, args);
        var pool = _pool;
        if (pool is null) return;
        var usable = pool.Where(n => !n.IsMeta && !cfg.BlockedNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        if (usable.Count < 2) return;
        var pinned = usable.Where(n => app.AllowedNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        void Form(string action, string label, string cls = "ghost") =>
            sb.Append("<form method=post action=").Append(action).Append("><input type=hidden name=tab value=apps><input type=hidden name=folder value=\"")
              .Append(E(app.Folder)).Append("\"><button class=").Append(cls).Append('>').Append(E(label)).Append("</button></form>");
        sb.Append("<details class=node-help><summary>").Append(E(S("node_help_title"))).Append("</summary><p class=hint>")
          .Append(E(S("node_help_hint"))).Append("</p><p class=hint>")
          .Append(E(pinned.Count == 0 ? S("node_help_any")
              : S("node_help_now", string.Join(", ", pinned.Take(3).Select(n => n.Remark.Length > 0 ? n.Remark : n.Tag)) + (pinned.Count > 3 ? " …" : ""))))
          .Append("</p><div class=node-help-actions>");
        Form("/apps/node-next", S("node_next_btn"));
        if (pinned.Count > 0 || app.UnsuitableNodes.Count > 0) Form("/apps/node-reset", S("node_reset_btn"));
        sb.Append("</div><form class=node-word method=post action=/apps/node-word><input type=hidden name=tab value=apps><input type=hidden name=folder value=\"")
          .Append(E(app.Folder)).Append("\"><input type=text name=word minlength=2 maxlength=40 required placeholder=\"").Append(E(S("node_word_ph")))
          .Append("\"><button class=ghost>").Append(E(S("node_word_btn"))).Append("</button></form>");
        if (app.UnsuitableNodes.Count > 0)
            sb.Append("<p class=hint>").Append(E(S("node_unsuitable", app.UnsuitableNodes.Count))).Append("</p>");
        sb.Append("</details>");
    }

    private void RenderRecommended(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S, string tab)
    {
        List<InstalledAppCatalog.Entry> todo;
        try
        {
            todo = InstalledAppCatalog.Recommended()
                .Where(e => AppIdentity.Find(cfg.Apps, e.Path) is null && !AppCoverage.IsEntryCovered(cfg, e.Path)).ToList();
        }
        catch { return; }
        if (todo.Count == 0) return;
        var names = string.Join(", ", todo.Take(6).Select(e => e.Name)) + (todo.Count > 6 ? " …" : "");
        sb.Append("<section class=recommend><h2>").Append(E(S("rec_title", []))).Append("</h2><p class=lede>")
          .Append(E(S("rec_lede", [names]))).Append("</p><form method=post action=/apps/add-recommended><input type=hidden name=tab value=")
          .Append(E(tab)).Append("><button class=big>").Append(E(S("rec_btn", [todo.Count]))).Append("</button></form></section>");
    }

    private static void AppendAppRecheck(StringBuilder sb, CehoConfig cfg, AppEntry app, string tab, string? wizard = null)
    {
        sb.Append("<form class=app-actions method=post action=/apps/check><input type=hidden name=tab value=\"").Append(E(tab))
          .Append("\"><input type=hidden name=folder value=\"").Append(E(app.Folder)).Append("\">");
        if (wizard is not null) sb.Append("<input type=hidden name=wizard value=\"").Append(E(wizard)).Append("\">");
        sb.Append("<button class=ghost aria-label=\"").Append(E(AppObservation.Text(cfg, "Проверить снова: ", "Recheck: ") + app.Label))
          .Append("\">").Append(E(AppObservation.Text(cfg, "Проверить снова", "Recheck"))).Append("</button></form>");
    }

    private int _doctorRunning;
    private bool? _doctorSimple;

    private bool DoctorResultIsFresh(CehoConfig cfg)
    {
        var now = DateTime.UtcNow;
        return _doctor is not null && _doctorSimple == cfg.SimplePanel && _doctorAtUtc <= now
            && now - _doctorAtUtc < TimeSpan.FromMinutes(10)
            && File.GetLastWriteTimeUtc(_configPath) <= _doctorAtUtc;
    }

    private void EnsureFreshDoctor(CehoConfig cfg)
    {
        if (Jobs.Active(JobRestore) is not null) return;
        if (DoctorResultIsFresh(cfg)) return;
        if (Jobs.Active(JobDoctor) is not null) return;
        if (Interlocked.Exchange(ref _doctorRunning, 1) == 1) return;
        var generation = Volatile.Read(ref _observationGeneration);
        _ = Task.Run(async () =>
        {
            try
            {
                var configAt = File.GetLastWriteTimeUtc(_configPath);
                var result = await Doctor.CheckAsync(CehoConfig.Load(_configPath), Root, Tools(), null, cfg.SimplePanel);
                // A check started before rule changes or restoration cannot certify the new state.
                lock (_appsLiveSync)
                {
                    if (generation != Volatile.Read(ref _observationGeneration)
                        || configAt != File.GetLastWriteTimeUtc(_configPath) || Jobs.Active(JobRestore) is not null) return;
                    Remember(result);
                    _doctorSimple = cfg.SimplePanel;
                }
            }
            catch (Exception ex) { Log.Warn($"самопроверка панели не завершилась: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _doctorRunning, 0); }
        });
    }

    private void RenderCheckSummary(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        EnsureFreshDoctor(cfg);
        sb.Append("<section data-live=check><h2>").Append(E(S("selfcheck_title", []))).Append("</h2>");
        var report = _doctor;
        if (report is null || _doctorSimple != cfg.SimplePanel)
        {
            sb.Append("<div class=\"line wait\"><span><span class=dot></span> ").Append(E(S("check_running", [])))
              .Append("</span></div></section>");
            return;
        }

        var preflight = (PanelPreflight?.Invoke(cfg) ?? Preflight.Run(cfg, Root, cfg.SimplePanel))
            .Where(c => c.Level != Preflight.Level.Ok).ToList();
        var shown = preflight.Select(c => c.Title).ToHashSet();
        var found = report.Checks.Where(c => c.Level != Preflight.Level.Ok && !shown.Contains(c.Title))
            .GroupBy(c => c.Title).Select(g => g.First()).ToList();
        var allProblems = preflight.Concat(found).ToList();
        var problems = allProblems.Count;
        var fresh = DoctorResultIsFresh(cfg);
        var cls = !fresh ? "wait" : problems == 0 ? "on" : allProblems.Any(c => c.Level == Preflight.Level.Blocker) ? "bad" : "warn";
        sb.Append("<div class=\"line ").Append(cls).Append("\"><span><span class=dot></span> ")
          .Append(E(!fresh ? AppObservation.Text(cfg, "Обновляем диагностику; ниже предыдущий результат", "Refreshing diagnostics; previous result shown below")
              : problems == 0 ? AppObservation.Text(cfg, "Общие проверки без замечаний", "General checks found no issues") : S("check_found", [problems])))
          .Append("</span><a class=btnlink href=\"/?tab=doctor\">").Append(E(S("check_open", []))).Append("</a></div>")
          .Append("<p class=observation-meta>").Append(E(AppObservation.Text(cfg, "Последняя диагностика: ", "Last diagnostics: ")
              + _doctorAtUtc.ToLocalTime().ToString("dd.MM HH:mm:ss"))).Append("</p><p class=hint>")
          .Append(E(AppObservation.Text(cfg, "Общая диагностика не заменяет проверку трафика каждой программы.",
              "General diagnostics do not replace a traffic check for each app."))).Append("</p>");
        if (found.Count > 0 && cfg.SimplePanel)
        {
            void Items(IEnumerable<Preflight.Check> items)
            {
                sb.Append("<ul class=findings>");
                foreach (var c in items)
                    sb.Append("<li><b>").Append(E(c.Title)).Append("</b>").Append(c.Fix is null ? "" : "<br>" + E(c.Fix)).Append("</li>");
                sb.Append("</ul>");
            }
            Items(found.Take(3));
            if (found.Count > 3)
            {
                sb.Append("<details class=more-findings><summary>").Append(E(S("check_more", [found.Count - 3]))).Append("</summary>");
                Items(found.Skip(3));
                sb.Append("</details>");
            }
        }
        sb.Append("</section>");
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
          .Append("</button>")
          .Append("<button type=submit formaction=\"/doctor/ping\" class=ghost>").Append(E(S("doc_ping_btn", [])))
          .Append("</button></form>");

        if (_lastPing is not null)
        {
            RenderPingCard(sb, _lastPing, S);
        }

        sb.Append("</section>");
        RenderLiveApps(sb, cfg, _state(), LeakGuard.IsActive(Root), S, "doctor");
        sb.Append("<section data-live=doctor><h2>").Append(E(AppObservation.Text(cfg, "Общая диагностика", "General diagnostics"))).Append("</h2>");
        EnsureFreshDoctor(cfg);
        var report = _doctor;
        if (report is null)
        {
            sb.Append("<p class=hint>").Append(E(S("check_running", []))).Append("</p></section>");
            return;
        }

        var reportFresh = DoctorResultIsFresh(cfg);
        var head = report.Healthy
            ? (report.Warnings > 0 ? S("doc_warnings", [report.Warnings]) : AppObservation.Text(cfg, "Общие проверки без замечаний", "General checks found no issues"))
            : S("pf_blockers", [report.Blockers]);

        sb.Append("<div class=\"status ").Append(!reportFresh ? "wait" : report.Healthy ? report.Warnings > 0 ? "wait" : "on" : "bad")
          .Append("\"><span class=dot></span><b>").Append(E(head)).Append("</b><span class=detail>")
          .Append(E(S("doc_when", [_doctorAtUtc.ToLocalTime().ToString("dd.MM HH:mm")])))
          .Append("</span></div>");

        if (!reportFresh)
            sb.Append("<p class=hint>").Append(E(AppObservation.Text(cfg,
                "Это предыдущий результат. Новая диагностика ещё не завершена.",
                "This is the previous result. A new diagnostic check has not completed yet."))).Append("</p>");

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

    private static void RenderPingCard(StringBuilder sb, ConnPingReport ping, Func<string, object[], string> S)
    {
        sb.Append("<div class=ping-card>");
        sb.Append("<h3>").Append(E(S("ping_card_title", []))).Append("</h3>");
        sb.Append("<p class=hint>").Append(E(S("ping_target_label", []))).Append(": <code>").Append(E(ping.Direct.Target)).Append("</code></p>");

        sb.Append("<div class=ping-pairs>");

        // 1 этап: напрямую
        var dirOk = ping.Direct.Ok;
        sb.Append("<div class=\"ping-result ").Append(dirOk ? "ok" : "bad").Append("\">");
        sb.Append("<div style=\"font-weight:600; margin-bottom:4px;\">").Append(E(S("ping_direct_title", []))).Append("</div>");
        if (dirOk)
        {
            sb.Append("<span style=\"font-size:20px; font-weight:bold; color:").Append(ping.Direct.SuccessPercent >= 100 ? "#059669" : "#d97706").Append(";\">")
              .Append(ping.Direct.SuccessPercent).Append("%</span> ")
              .Append("<span class=hint>(").Append(ping.Direct.SuccessCount).Append("/").Append(ping.Direct.TotalAttempts).Append(")</span> · ")
              .Append("<b>").Append(ping.Direct.AvgMs).Append(" мс</b>");
        }
        else
        {
            sb.Append("<span style=\"font-weight:bold; color:#dc2626;\">0%</span> · <span style=\"color:#dc2626;\">")
              .Append(E(ping.Direct.LastError ?? "ошибка")).Append("</span>");
        }
        sb.Append("</div>");

        // 2 этап: через ноду
        var proxyOk = ping.Proxy.Ok;
        sb.Append("<div class=\"ping-result ").Append(proxyOk ? "ok" : "bad").Append("\">");
        sb.Append("<div style=\"font-weight:600; margin-bottom:4px;\">").Append(E(S("ping_proxy_title", []))).Append("</div>");
        if (proxyOk)
        {
            sb.Append("<span style=\"font-size:20px; font-weight:bold; color:").Append(ping.Proxy.SuccessPercent >= 100 ? "#059669" : "#d97706").Append(";\">")
              .Append(ping.Proxy.SuccessPercent).Append("%</span> ")
              .Append("<span class=hint>(").Append(ping.Proxy.SuccessCount).Append("/").Append(ping.Proxy.TotalAttempts).Append(")</span> · ")
              .Append("<b>").Append(ping.Proxy.AvgMs).Append(" мс</b>");
        }
        else
        {
            sb.Append("<span style=\"font-weight:bold; color:#dc2626;\">0%</span> · <span style=\"color:#dc2626;\">")
              .Append(E(ping.Proxy.LastError ?? "ошибка")).Append("</span>");
        }
        sb.Append("</div>");

        sb.Append("</div>");

        // Вердикт
        var verdict = (ping.Direct.Ok, ping.Proxy.Ok) switch
        {
            (true, true) => S("ping_verdict_both", []),
            (true, false) => S("ping_verdict_proxy_down", []),
            _ when TunCleanup.LogShowsStuckAdapter() => S("ping_verdict_tun_hijack", []),
            _ => S("ping_verdict_direct_down", []),
        };
        sb.Append("<p style=\"margin-top:12px; margin-bottom:0; font-weight:500;\">").Append(E(verdict)).Append("</p>");

        sb.Append("</div>");
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
            var selfFix = c.Repair != Repair.None && c.Repair != Repair.Elevate;
            if (c.Fix is not null)
                sb.Append("<span class=\"why").Append(selfFix ? " can" : "").Append("\">")
                  .Append(E(selfFix ? c.Fix : S("doc_what_to_do", [c.Fix])))
                  .Append("</span>");
            if (c.Repair == Repair.Elevate)
                sb.Append("<form method=post action=/elevate><input type=hidden name=tab value=doctor><button>")
                  .Append(E(S("btn_elevate", []))).Append("</button></form>");
            sb.Append("</div></li>");
        }
        sb.Append("</ul>");
    }

    private static void AppendSiteMode(
        StringBuilder sb, Func<string, object[], string> S, string mode, string labelKey, bool selected)
    {
        var label = E(S(labelKey, []));
        if (selected)
        {
            sb.Append("<span class=mode-on>").Append(label).Append("</span>");
            return;
        }

        sb.Append("<form method=post action=/sites/mode><input type=hidden name=tab value=sites>")
          .Append("<input type=hidden name=mode value=\"").Append(mode).Append("\">")
          .Append("<button class=ghost>").Append(label).Append("</button></form>");
    }

    private void RenderSites(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        var only = cfg.SitesOnly;
        var (countries, countriesLoaded) = SiteCountryChoices(cfg);
        sb.Append("<section><h2>").Append(E(S("sites_title", []))).Append("</h2>");
        sb.Append("<div class=modes>");
        AppendSiteMode(sb, S, CehoConfig.SiteModeExcept, "sites_mode_except", !only);
        AppendSiteMode(sb, S, CehoConfig.SiteModeOnly, "sites_mode_only", only);
        sb.Append("</div>");
        sb.Append("<p class=lede>").Append(E(S(only ? "sites_lede_only" : "sites_lede_except", []))).Append("</p>");
        sb.Append("<form class=\"row app-add\" method=post action=/sites/add><input type=hidden name=tab value=sites>")
          .Append("<label class=sr-only for=direct-site>").Append(E(S("nav_sites", []))).Append("</label>")
          .Append("<input id=direct-site type=text name=site placeholder=\"")
          .Append(E(S("sites_placeholder", []))).Append("\" autofocus>");
        AppendCountrySelect(sb, cfg, S, countries, null);
        sb.Append("<button>").Append(E(S("btn_add", []))).Append("</button></form>");
        sb.Append("<form method=post action=/sites/preset><input type=hidden name=tab value=sites><button>")
          .Append(E(S(only ? "sites_preset_btn_only" : "sites_preset_btn_except", [])))
          .Append("</button></form><p class=hint>")
          .Append(E(S(only ? "sites_preset_hint_only" : "sites_preset_hint_except", [])))
          .Append("</p>");
        if (countries.Count == 0)
            sb.Append("<p class=hint>").Append(E(S(countriesLoaded ? "sites_countries_none" : "sites_countries_loading", []))).Append("</p>");

        if (cfg.DirectSites.Count == 0)
            sb.Append("<p class=empty>").Append(E(S(only ? "sites_empty_only" : "sites_empty", []))).Append("</p>");
        else
        {
            sb.Append("<div class=scroll><table class=t-apps><tr><th>")
              .Append(E(S("nav_sites", []))).Append("</th><th>")
              .Append(E(S("sites_exit", []))).Append("</th><th></th></tr>");
            foreach (var site in cfg.DirectSites)
            {
                var chosen = SingBoxConfigGenerator.SiteCountry(cfg, site);
                sb.Append("<tr><td class=path>").Append(E(site)).Append("</td><td>")
                  .Append("<form class=row method=post action=/sites/country><input type=hidden name=tab value=sites>")
                  .Append("<input type=hidden name=site value=\"").Append(E(site)).Append("\">");
                AppendCountrySelect(sb, cfg, S, countries, chosen);
                sb.Append("<button class=ghost>").Append(E(S("btn_save", []))).Append("</button></form>")
                  .Append("</td><td class=actions>")
                  .Append("<form method=post action=/sites/remove><input type=hidden name=tab value=sites>")
                  .Append("<input type=hidden name=site value=\"").Append(E(site))
                  .Append("\"><button class=danger>").Append(E(S("btn_remove", []))).Append("</button></form>")
                  .Append("</td></tr>");
            }
            sb.Append("</table></div>");
        }
        sb.Append("</section>");
    }

    private (List<(string Code, string Label)> Choices, bool Loaded) SiteCountryChoices(CehoConfig cfg)
    {
        var pool = _pool;
        var loading = Jobs.Active(JobPool);
        if (pool is null && loading is null && cfg.Subscriptions.Any(s => s.Enabled))
            StartPoolJob(cfg);

        var choices = new List<(string Code, string Label)>();
        if (pool is null) return (choices, false);
        foreach (var group in pool.Where(n => !n.IsMeta)
                     .GroupBy(n => n.CountryCode ?? "")
                     .Where(g => DirectSites.NormalizeCountry(g.Key) is not null)
                     .OrderBy(g => CountryResolver.DisplayName(g.Key, cfg.Language) ?? g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var code = DirectSites.NormalizeCountry(group.Key)!;
            var name = CountryResolver.DisplayName(code, cfg.Language) ?? code;
            choices.Add((code, CountryResolver.Flag(code) + " " + name));
        }
        return (choices, true);
    }

    private static void AppendCountrySelect(
        StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S,
        IReadOnlyList<(string Code, string Label)> countries, string? selected)
    {
        if (countries.Count == 0 && selected is null) return;
        sb.Append("<label class=sr-only>").Append(E(S("sites_exit", []))).Append("</label>");
        sb.Append("<select name=country><option value=\"\">")
          .Append(E(S(cfg.SitesOnly ? "sites_exit_pool" : "sites_exit_direct", []))).Append("</option>");
        var seen = false;
        foreach (var (code, label) in countries)
        {
            if (string.Equals(code, selected, StringComparison.OrdinalIgnoreCase)) seen = true;
            sb.Append("<option value=\"").Append(E(code)).Append('"')
              .Append(string.Equals(code, selected, StringComparison.OrdinalIgnoreCase) ? " selected" : "")
              .Append('>').Append(E(label)).Append("</option>");
        }
        if (selected is not null && !seen)
        {
            var name = CountryResolver.DisplayName(selected, cfg.Language) ?? selected;
            sb.Append("<option value=\"").Append(E(selected)).Append("\" selected>")
              .Append(E(CountryResolver.Flag(selected) + " " + name)).Append("</option>");
        }
        sb.Append("</select>");
    }

    internal Func<IReadOnlyList<AiTools.Found>>? DetectedTools { get; set; }
    internal Func<CehoConfig, IReadOnlyList<string>>? RunningBrowsers { get; set; }

    private IReadOnlyList<InstalledAppCatalog.Entry> PanelInstalledApps(CehoConfig cfg)
    {
        try { return InstalledApps?.Invoke(cfg.Language) ?? InstalledAppCatalog.Detect(cfg.Language); }
        catch { return Array.Empty<InstalledAppCatalog.Entry>(); }
    }

    private void RenderApps(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S,
        string? tunnelFolder, string? pickedPath = null)
    {
        if (!string.IsNullOrEmpty(tunnelFolder))
        {
            RenderAppTunnel(sb, cfg, S, tunnelFolder);
            return;
        }

        sb.Append("<section><h2>").Append(E(S("apps_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("apps_lede", []))).Append("</p>");

        var liveBrowsers = RunningBrowsers?.Invoke(cfg) ?? IsolatedAppBounce.RunningBrowserLabels(cfg);
        if (_state().Running && liveBrowsers.Count > 0)
        {
            sb.Append("<div class=\"flash warn\"><b>").Append(E(S("apps_browser_live_title", [])))
              .Append("</b>").Append(E(S("apps_browser_live", new object[] { string.Join(", ", liveBrowsers) })))
              .Append("<form method=post action=/apps/bounce><input type=hidden name=tab value=apps>")
              .Append("<button>").Append(E(S("btn_bounce_network", []))).Append("</button></form></div>");
        }

        var installed = PanelInstalledApps(cfg);
        var state = _state();
        RenderRecommended(sb, cfg, S, "apps");
        TunnelUi.Render(sb, cfg, installed, "apps", state.Running, Volatile.Read(ref _pending) > 0, AppRulesPending && (Jobs.Active(JobApply) is not null || Jobs.Active(JobPower) is not null || Jobs.Active(JobRestore) is not null), pickedPath);
        sb.Append("<div data-live=app-cards id=added-apps><h2 class=tunnel-added-title>").Append(E(AppObservation.Text(cfg, "Добавленные программы", "Added apps"))).Append("</h2>");
        var live = state.Running ? AppsLive() : null;
        var guarded = LeakGuard.IsActive(Root);
        if (cfg.Apps.Count == 0)
            sb.Append("<p class=empty>").Append(E(S("apps_empty", []))).Append("</p>");
        else
        {
            sb.Append("<div class=app-cards>");
            foreach (var a in cfg.Apps)
            {
                var info = live?.FirstOrDefault(l => l.Folder == a.Folder);
                var observation = AppObservation.Evaluate(cfg, a, info, state.Running, guarded, _appsLiveAtUtc, DateTime.UtcNow, _appsLiveFailed, AppRulesPending);
                sb.Append("<article id=\"app-").Append(AppIdentity.Id(a)).Append("\" data-app-id=\"").Append(AppIdentity.Id(a))
                  .Append("\" data-app-path=\"").Append(E(AppIdentity.PathOf(a))).Append("\" data-rule-state=\"")
                  .Append(IsAppRuleApplied(a) ? "applied" : AppRulesPending ? "pending" : state.Running ? "unknown" : "inactive")
                  .Append("\" tabindex=-1 class=\"app-card ").Append(observation.Css).Append("\"><div class=app-identity><span class=dot aria-hidden=true></span>");
                AppIcon(sb, AppIcons.Source(a, installed), a.Label);
                sb.Append("<h3 title=\"").Append(E(a.Folder)).Append("\">").Append(E(a.Label)).Append("</h3></div>");
                if (a.VersionAgnostic) sb.Append("<span class=tag>Microsoft Store</span>");
                if (a.SingleFile) sb.Append("<span class=tag>").Append(E(S("col_file", []))).Append("</span>");
                RenderAppObservation(sb, cfg, a, info, observation, state, withCountry: true);
                sb.Append("<div class=app-card-actions>");
                AppendAppRecheck(sb, cfg, a, "apps");
                sb.Append("<a class=ghost href=\"/?tab=apps&amp;tunnel=")
                  .Append(Uri.EscapeDataString(a.Folder)).Append("\">").Append(E(S("btn_tunnel", []))).Append("</a>")
                  .Append("<details class=rename><summary>").Append(E(S("btn_rename", []))).Append("</summary>")
                  .Append("<form class=row method=post action=/apps/rename><input type=hidden name=tab value=apps>")
                  .Append("<input type=hidden name=folder value=\"").Append(E(a.Folder)).Append("\">")
                  .Append("<label class=sr-only for=\"rename-").Append(E(Uri.EscapeDataString(a.Folder))).Append("\">")
                  .Append(E(S("rename_app_ask", []))).Append("</label>")
                  .Append("<input id=\"rename-").Append(E(Uri.EscapeDataString(a.Folder))).Append("\" type=text name=displayName value=\"").Append(E(a.Label))
                  .Append("\" placeholder=\"").Append(E(S("rename_app_ask", []))).Append("\">")
                  .Append("<button class=ghost>").Append(E(S("btn_save", []))).Append("</button></form></details>")
                  .Append("<details class=remove-app><summary>").Append(E(S("btn_remove", []))).Append("</summary><p class=hint>")
                  .Append(E(AppObservation.Text(cfg, "Удаление исключит программу из правил VPN; она может подключаться напрямую. Если это последняя программа, туннель остановится.",
                      "Removing this app excludes it from VPN rules; it may connect directly. Removing the last app stops the tunnel.")))
                  .Append("</p><form method=post action=/apps/remove><input type=hidden name=tab value=apps>")
                  .Append("<input type=hidden name=folder value=\"").Append(E(a.Folder)).Append("\"><button class=danger>")
                  .Append(E(S("btn_remove", []))).Append("</button></form></details></div></article>");
            }
            sb.Append("</div></div>");
        }

        if (cfg.Apps.Count == 0) sb.Append("</div>");

        RenderDetected(sb, cfg, S);
        sb.Append("</section>");
    }

    private void RenderAppTunnel(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S,
        string folder)
    {
        var app = cfg.Apps.FirstOrDefault(a =>
            AppIdentity.SameConfiguredPath(a.Folder, folder));
        if (app is null)
        {
            sb.Append("<section><h2>").Append(E(S("apps_title", []))).Append("</h2>");
            sb.Append("<p class=empty>").Append(E(S("app_tunnel_missing", []))).Append("</p>");
            sb.Append("<p><a href=\"/?tab=apps\">").Append(E(S("app_tunnel_back", []))).Append("</a></p>");
            sb.Append("</section>");
            return;
        }

        sb.Append("<section><h2>").Append(E(S("app_tunnel_title", new object[] { app.Label }))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("app_tunnel_lede", []))).Append("</p>");
        sb.Append("<p><a href=\"/?tab=apps\">").Append(E(S("app_tunnel_back", []))).Append("</a></p>");

        sb.Append("<aside class=consequences><b>").Append(E(AppObservation.Text(cfg, "Перед изменением маршрута", "Before changing routing")))
          .Append("</b><p>").Append(E(AppObservation.Text(cfg,
              $"Изменение относится к программе «{app.Label}». Если туннель запущен, применение правил переподключит его; соединения других выбранных программ тоже могут прерваться. После применения нужна новая проверка трафика.",
              $"The change applies to {app.Label}. If the tunnel is running, applying rules reconnects it; other selected apps may also lose connections briefly. Recheck app traffic after applying.")))
          .Append("</p><p>").Append(E(AppObservation.Text(cfg,
              "Запрет интернета блокирует программу. Снятие запрета возвращает настроенный VPN-маршрут и правила сайтов, а не гарантирует прямой доступ.",
              "Blocking Internet denies access for this app. Removing the block restores its configured VPN route and site rules; it does not guarantee direct access.")))
          .Append("</p>");
        if (Os.IsWindows)
            sb.Append("<p>").Append(E(AppObservation.StopConsequence(cfg, true, LeakGuard.IsActive(Root)))).Append("</p>");
        sb.Append("</aside>");

        sb.Append("<div class=\"line ").Append(app.NoInternet ? "off" : "on").Append("\"><span><span class=dot></span> ")
          .Append(E(S(app.NoInternet ? "app_offline_on" : "app_offline_off", []))).Append("</span>")
          .Append("<form method=post action=/apps/offline><input type=hidden name=tab value=apps>")
          .Append("<input type=hidden name=folder value=\"").Append(E(app.Folder)).Append("\">")
          .Append(app.NoInternet ? "" : "<input type=hidden name=enable value=1>")
          .Append("<button class=").Append(app.NoInternet ? "ghost" : "danger").Append('>')
          .Append(E(S(app.NoInternet ? "app_offline_del" : "app_offline_add", [])))
          .Append("</button></form></div>");
        if (app.NoInternet)
        {
            sb.Append("<p class=hint>").Append(E(S("app_offline_hint", []))).Append("</p>");
            sb.Append("</section>");
            return;
        }

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
            sb.Append("</section>");
            return;
        }

        var groups = pool.Where(n => !n.IsMeta)
            .GroupBy(n => n.CountryCode ?? CountryResolver.Unknown)
            .OrderByDescending(g => g.Count())
            .ToList();
        var selected = app.AllowedNodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        sb.Append("<form method=post action=/apps/tunnel><input type=hidden name=tab value=apps>");
        sb.Append("<input type=hidden name=folder value=\"").Append(E(app.Folder)).Append("\">");
        sb.Append("<input type=hidden name=all value=\"")
          .Append(E(string.Join("\n", groups.SelectMany(g => g).Select(n => n.Key)))).Append("\">");

        foreach (var g in groups)
        {
            var countryName = g.Key == CountryResolver.Unknown
                ? S("country_unknown", [])
                : g.First().CountryName ?? g.Key;
            var here = g.Count(n => selected.Contains(n.Key));
            sb.Append("<details class=nodes").Append(here > 0 ? " open" : "")
              .Append("><summary>");
            sb.Append("<span class=flag>").Append(CountryResolver.Flag(g.Key)).Append("</span> ")
              .Append(E(countryName)).Append(" · ").Append(g.Count());
            if (here > 0)
                sb.Append(" · ").Append(E(S("app_tunnel_pinned", new object[] { here })));
            sb.Append("</summary><div class=scroll><table class=t-nodes>");
            sb.Append("<tr><th>").Append(E(S("col_use", []))).Append("</th><th>")
              .Append(E(S("col_node", []))).Append("</th><th>").Append(E(S("col_address", [])))
              .Append("</th><th>").Append(E(S("col_protocols", []))).Append("</th><th>")
              .Append(E(S("col_source", []))).Append("</th></tr>");

            foreach (var n in g.OrderBy(n => n.Remark, StringComparer.OrdinalIgnoreCase)
                               .ThenBy(n => n.Server, StringComparer.OrdinalIgnoreCase))
            {
                var on = selected.Contains(n.Key);
                sb.Append("<tr").Append(on ? "" : " class=off").Append("><td><label class=check>")
                  .Append("<input type=checkbox name=\"n_").Append(E(n.Key)).Append('"')
                  .Append(on ? " checked" : "").Append("></label></td>");
                sb.Append("<td>").Append(E(n.Remark.Length > 0 ? n.Remark : n.Tag)).Append("</td>");
                sb.Append("<td class=tag>").Append(E($"{n.Server}:{n.Port}")).Append("</td>");
                sb.Append("<td class=tag>").Append(E(n.Protocol.ToString())).Append("</td>");
                sb.Append("<td class=tag>").Append(E(n.Source ?? "—")).Append("</td></tr>");
            }
            sb.Append("</table></div></details>");
        }

        sb.Append("<button>").Append(E(S("btn_save", []))).Append("</button></form>");
        sb.Append("</section>");
    }

    private void RenderDetected(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        IReadOnlyList<AiTools.Found> found;
        try { found = DetectedTools?.Invoke() ?? AiTools.Detect(); }
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
        sb.Append("<div class=scroll><table class=t-apps><tr><th>")
          .Append(E(S("col_name", []))).Append("</th><th>")
          .Append(E(S("col_folder", []))).Append("</th><th></th></tr>");

        foreach (var tool in found)
        {
            var covered = AppCoverage.IsToolCovered(cfg, tool);

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
                  .Append(E(S("ai_script_warn", new object[]
                  {
                      Path.GetFileName(tool.Interpreter), AiTools.SuggestedCommand(tool),
                  })))
                  .Append("</span>");
            sb.Append("</td><td class=actions>");

            if (covered)
                sb.Append("<span class=tag>").Append(E(S("ai_added", []))).Append("</span>");
            else if (tool.Kind == AiTools.ToolKind.Script)
            {
                var command = AiTools.SuggestedCommand(tool);
                sb.Append("<code>chp wrap ").Append(E(command)).Append("</code><br><span class=tag>")
                  .Append(E(S("run_once", []))).Append(": chp run ").Append(E(command)).Append("</span>");
            }
            else
                sb.Append("<form method=post action=/apps/detected><input type=hidden name=tab value=apps>")
                  .Append("<input type=hidden name=path value=\"").Append(E(tool.Path)).Append("\">")
                  .Append("<input type=hidden name=name value=\"").Append(E(tool.Name)).Append("\">")
                  .Append("<button>").Append(E(S("ai_add", []))).Append("</button></form>");

            sb.Append("</td></tr>");
        }
        sb.Append("</table></div>");
    }

    private static string ExpiryText(DateTime expiresUtc, Func<string, object[], string> S)
    {
        var days = SubscriptionInfo.DaysLeft(expiresUtc);
        var date = expiresUtc.ToLocalTime().ToString("dd.MM.yyyy");
        return days < 0
            ? S("sub_expired_on", new object[] { date })
            : S("sub_expires_on", new object[] { date, days });
    }

    private void RenderSubs(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("nav_subs", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("subs_pool", []))).Append("</p>");

        if (cfg.Subscriptions.Count == 0)
            sb.Append("<p class=empty>").Append(E(S("subs_empty", []))).Append("</p>");
        else
        {
            sb.Append("<div class=scroll><table class=t-subs><tr><th>")
              .Append(E(S("col_use", []))).Append("</th><th>")
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

                sb.Append("<td><span class=uname>").Append(E(s.Name)).Append("</span>");
                if (SubscriptionKind.IsNaive(s))
                    sb.Append(" <span class=\"tag kind-naive\">").Append(E(S("subs_kind_naive", []))).Append("</span>");
                else
                    sb.Append(" <span class=\"tag kind-sub\">").Append(E(S("subs_kind_sub", []))).Append("</span>");
                if (s.LastNodes is { } nodes)
                    sb.Append("<br><span class=tag>").Append(E(S("sub_nodes_n", new object[] { nodes })))
                      .Append("</span>");
                if (SupportReport.HasInsecureTls(s, Root))
                    sb.Append(" <span class=\"tag bad tls-warning\" title=\"")
                      .Append(E(SafetyPanel.T(cfg.Language, "Подлинность сервера не проверяется; возможен перехват соединения.",
                          "The server identity is not verified; interception is possible."))).Append("\">")
                      .Append(E(SafetyPanel.T(cfg.Language, "Проверка TLS отключена", "TLS verification disabled"))).Append("</span>");
                sb.Append("<div class=path>").Append(E(cfg.SimplePanel ? HostOf(s.Url) : MaskUrl(s.Url))).Append("</div></td>");

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
                sb.Append("<button type=button class=ghost data-sub-edit=\"")
                  .Append(SubDialogId(s.Name)).Append("\">")
                  .Append(E(S("btn_edit", []))).Append("</button>");
                sb.Append("<form method=post action=/subs/remove data-confirm=\"")
                  .Append(E(SafetyPanel.T(cfg.Language, "Удалить подписку «" + s.Name + "»? Её настройки и локальный кэш будут удалены.",
                      "Delete subscription “" + s.Name + "”? Its settings and local cache will be removed.")))
                  .Append("\"><input type=hidden name=tab value=subs>")
                  .Append("<input type=hidden name=name value=\"").Append(E(s.Name))
                  .Append("\"><button class=danger>").Append(E(S("btn_delete", []))).Append("</button></form>");
                sb.Append("</td></tr>");
            }
            sb.Append("</table></div>");
            sb.Append("<p class=hint>").Append(E(S("subs_toggle_hint", []))).Append("</p>");
            sb.Append("<p class=hint>").Append(E(S("subs_expiry_hint", []))).Append("</p>");

            sb.Append("<div class=sub-modals>");
            foreach (var s in cfg.Subscriptions)
            {
                sb.Append("<dialog id=\"").Append(SubDialogId(s.Name)).Append("\" class=sub-modal data-unsaved-confirm=\"")
                  .Append(E(SafetyPanel.T(cfg.Language, "Закрыть без сохранения изменений?", "Discard unsaved changes?"))).Append("\">");
                sb.Append("<h3 class=modal-title>").Append(E(S("subs_edit_title", new object[] { s.Name }))).Append("</h3>");
                RenderSubForm(sb, S, "/subs/save", "btn_save", s, s.Name, editPassword: true, inModal: true, cfg.Language, cfg.SimplePanel);
                sb.Append("</dialog>");
            }
            sb.Append("</div>");
        }

        sb.Append("<form class=row method=post action=/subs/check><input type=hidden name=tab value=subs>")
          .Append("<button class=ghost>").Append(E(S("sub_check", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("sub_checking", []))).Append("</p>");

        sb.Append("<h3>").Append(E(S("subs_add_title", []))).Append("</h3>");
        RenderSubForm(sb, S, "/subs/add", "btn_add_sub", null, null, editPassword: false, inModal: false, cfg.Language, cfg.SimplePanel);

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
        sb.Append("<form method=post action=/exit/save><input type=hidden name=tab value=exit>");

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

            var measured = _countries?.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase)
                ?? NodeProbe.Summarize(pool, cfg, _liveLatency)
                    .ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

            sb.Append("<input type=hidden name=call value=\"")
              .Append(E(string.Join(",", groups.Select(g => g.Key)))).Append("\">");
            sb.Append("<div class=scroll><table class=t-countries><tr><th>")
              .Append(E(S("col_use", []))).Append("</th><th>")
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
            sb.Append("</table></div>");

            sb.Append("<p class=hint>").Append(E(S("pool_from", new object[]
            {
                pool.Count, _poolAtUtc.ToLocalTime().ToString("HH:mm:ss"),
            }))).Append("</p>");

            RenderNodes(sb, cfg, groups, S, _liveLatency);
        }

        sb.Append("<div class=row><button class=ghost formaction=/pool/refresh formnovalidate>")
          .Append(E(S("btn_pool_refresh", []))).Append("</button>")
          .Append("<button class=ghost formaction=/countries/refresh formnovalidate>")
          .Append(E(S("btn_measure", []))).Append("</button></div>");
        sb.Append("<p class=hint>").Append(E(S("udp_not_measured", []))).Append("</p>");

        sb.Append("<h2>").Append(E(S("check_title", []))).Append("</h2>");
        sb.Append("<div class=stack>");
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
          .Append(cfg.TimeoutSeconds.ToString()).Append("\" placeholder=\"45\"></label>");
        sb.Append("<p class=hint>").Append(E(S("timeout_hint", []))).Append("</p></div>");
        sb.Append("<div class=save-bar><button>").Append(E(S("btn_save_all", []))).Append("</button></div></form></section>");
    }

    /// <summary>
    /// Отдельные ноды: страна может быть разрешена целиком, а одну ноду из неё
    /// нужно убрать — например, она отвечает, но работает плохо.
    /// </summary>
    private static void RenderNodes(
        StringBuilder sb, CehoConfig cfg,
        List<IGrouping<string, ProxyNode>> groups, Func<string, object[], string> S,
        IReadOnlyDictionary<string, int>? liveByTag = null)
    {
        var blocked = cfg.BlockedNodes.Count;

        sb.Append("<h2>").Append(E(S("nodes_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("nodes_hint", []))).Append("</p>");
        if (blocked > 0)
            sb.Append("<p class=hint>").Append(E(S("nodes_off_now", new object[] { blocked }))).Append("</p>");

        sb.Append("<input type=hidden name=nall value=\"")
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
            sb.Append("</summary><div class=scroll><table class=t-nodes>");
            sb.Append("<tr><th>").Append(E(S("col_use", []))).Append("</th><th>")
              .Append(E(S("col_node", []))).Append("</th><th>").Append(E(S("col_address", [])))
              .Append("</th><th>").Append(E(S("col_protocols", []))).Append("</th><th>")
              .Append(E(S("col_best", []))).Append("</th><th>").Append(E(S("col_source", [])))
              .Append("</th></tr>");

            foreach (var n in g.OrderBy(n => n.Remark, StringComparer.OrdinalIgnoreCase))
            {
                var on = !SingBoxConfigGenerator.IsBlockedByHand(n, cfg);
                var slow = SingBoxConfigGenerator.IsTooSlow(n, cfg);
                var ms = NodeProbe.LatencyFor(n, cfg, liveByTag);

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
            sb.Append("</table></div></details>");
        }
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
        sb.Append("<p class=hint>").Append(E(S("browser_note", []))).Append("</p>");
        sb.Append("<form method=post action=/browser/test class=row>");
        sb.Append("<button type=submit>").Append(E(S("btn_naive_test", []))).Append("</button>");
        sb.Append("</form></section>");
    }

    private static bool TryBuildSubUrl(
        IReadOnlyDictionary<string, string> f,
        string remark,
        NaiveProxySettings? keepPasswordFrom,
        out string? url,
        out string? errorKey)
    {
        url = null;
        errorKey = null;
        var kind = f.GetValueOrDefault("kind", "sub").Trim();

        if (string.Equals(kind, "naive", StringComparison.OrdinalIgnoreCase))
        {
            var settings = new NaiveProxySettings
            {
                Enabled = true,
                Server = f.GetValueOrDefault("server", "").Trim(),
                Username = f.GetValueOrDefault("username", "").Trim(),
                ServerName = f.GetValueOrDefault("serverName", "").Trim(),
                AllowInsecure = f.ContainsKey("allowInsecure"),
                Remark = remark,
            };
            if (int.TryParse(f.GetValueOrDefault("port", "8443"), out var port))
                settings.Port = port;

            var pass = f.GetValueOrDefault("password", "").Trim();
            settings.Password = pass.Length > 0
                ? pass
                : keepPasswordFrom?.Password ?? "";

            if (!settings.IsConfigured)
            {
                errorKey = "naive_form_incomplete";
                return false;
            }

            url = NaiveProxyHelper.BuildUri(settings);
            return true;
        }

        url = f.GetValueOrDefault("url", "").Trim();
        if (url.Length == 0)
        {
            errorKey = "pf_no_subs_fix";
            return false;
        }

        if (NaiveProxyHelper.IsNaiveUri(url))
        {
            if (NaiveProxyHelper.TryParseUri(url, out var s) && s is not null)
            {
                url = NaiveProxyHelper.BuildUri(s);
            }
            else
            {
                errorKey = "naive_uri_bad";
                return false;
            }
        }
        else if (!SubscriptionParser.IsAcceptableSource(url))
        {
            errorKey = "sub_url_bad";
            return false;
        }

        return true;
    }

    private static void RenderSubForm(
        StringBuilder sb,
        Func<string, object[], string> S,
        string action,
        string buttonKey,
        SubscriptionEntry? edit,
        string? origName,
        bool editPassword,
        bool inModal,
        string language,
        bool simple)
    {
        string T(string ru, string en) => SafetyPanel.T(language, ru, en);
        var isNaive = edit is not null && SubscriptionKind.IsNaive(edit);
        NaiveProxySettings? naive = null;
        if (isNaive && NaiveProxyHelper.TryParseUri(edit!.Url, out var parsed))
            naive = parsed;

        var radioId = edit is null ? "add" : "e" + Math.Abs(StringComparer.Ordinal.GetHashCode(origName ?? edit.Name)).ToString();
        sb.Append("<form class=\"stack sub-add\" method=post action=").Append(action)
          .Append("><input type=hidden name=tab value=subs>");
        if (origName is not null)
            sb.Append("<input type=hidden name=origName value=\"").Append(E(origName)).Append("\">");

        sb.Append("<div class=kind-switch role=group aria-label=\"").Append(E(S("subs_kind_label", []))).Append("\">");
        sb.Append("<input type=radio name=kind id=").Append(radioId).Append("-sub value=sub")
          .Append(isNaive ? "" : " checked").Append(">");
        sb.Append("<label for=").Append(radioId).Append("-sub>").Append(E(S("subs_kind_sub", []))).Append("</label>");
        sb.Append("<input type=radio name=kind id=").Append(radioId).Append("-naive value=naive")
          .Append(isNaive ? " checked" : "").Append(">");
        sb.Append("<label for=").Append(radioId).Append("-naive>").Append(E(S("subs_kind_naive", []))).Append("</label>");
        sb.Append("</div>");

        sb.Append("<label class=field><span>").Append(E(S("col_name", []))).Append("</span><input type=text name=name");
        if (edit is not null)
            sb.Append(" value=\"").Append(E(edit.Name)).Append("\"");
        else
            sb.Append(" placeholder=\"").Append(E(S("subs_name_placeholder", []))).Append("\"");
        sb.Append("></label>");

        sb.Append("<div class=\"sub-add-panel sub-add-url\">");
        sb.Append("<label class=field><span>").Append(E(S("col_link", []))).Append("</span><input type=text name=url");
        if (edit is not null && !isNaive)
            sb.Append(" value=\"").Append(E(edit.Url)).Append("\"");
        else
            sb.Append(" placeholder=\"").Append(E(S("subs_url_placeholder", []))).Append("\"");
        sb.Append("></label>");
        sb.Append("<p class=hint>").Append(E(S("subs_kind_sub_hint", []))).Append("</p></div>");

        sb.Append("<div class=\"sub-add-panel sub-add-naive\">");
        sb.Append("<label class=field><span>").Append(E(S("naive_server", []))).Append("</span><input type=text name=server");
        if (naive is not null) sb.Append(" value=\"").Append(E(naive.Server)).Append("\"");
        sb.Append("></label>");
        sb.Append("<label class=field><span>").Append(E(S("naive_port", []))).Append("</span><input type=number name=port value=\"")
          .Append(naive?.Port.ToString() ?? "8443").Append("\" min=\"1\" max=\"65535\"></label>");
        sb.Append("<label class=field><span>").Append(E(S("naive_user", []))).Append("</span><input type=text name=username");
        if (naive is not null) sb.Append(" value=\"").Append(E(naive.Username)).Append("\"");
        sb.Append(" autocomplete=off></label>");
        sb.Append("<label class=field><span>").Append(E(S("naive_password", [])))
          .Append("</span><input type=password name=password autocomplete=new-password");
        if (editPassword && naive?.Password.Length > 0)
            sb.Append(" placeholder=\"").Append(E(S("naive_password_keep", []))).Append("\"");
        sb.Append("></label>");
        if (editPassword)
            sb.Append("<p class=hint>").Append(E(S("naive_password_keep", []))).Append("</p>");
        sb.Append("<label class=field><span>").Append(E(S("naive_sni", []))).Append("</span><input type=text name=serverName");
        if (naive?.ServerName is { Length: > 0 }) sb.Append(" value=\"").Append(E(naive.ServerName)).Append("\"");
        sb.Append(" placeholder=\"").Append(E(S("naive_sni_hint", []))).Append("\"></label>");
        if (naive is { AllowInsecure: true })
            sb.Append("<p class=\"flash err tls-warning\" role=status>")
              .Append(E(T("Проверка TLS-сертификата отключена (allowInsecure). Подлинность сервера не проверяется: соединение может быть перехвачено.",
                  "TLS certificate verification is disabled (allowInsecure). The server's identity is not verified: the connection could be intercepted."))).Append("</p>");
        if (simple)
        {
            // Simple mode must not silently clear a previously approved advanced setting.
            if (naive is { AllowInsecure: true }) sb.Append("<input type=hidden name=allowInsecure value=1>");
            sb.Append("<p class=hint>").Append(E(T("Настройки проверки TLS доступны в расширенном режиме.",
                "TLS verification settings are available in advanced mode."))).Append("</p>");
        }
        else
        {
            sb.Append("<details class=tls-advanced><summary>").Append(E(T("Дополнительно: безопасность TLS", "Advanced: TLS security"))).Append("</summary>");
            sb.Append("<label class=check><input type=checkbox name=allowInsecure")
              .Append(naive is { AllowInsecure: true } ? " checked" : "").Append("> ")
              .Append(E(T("Отключить проверку TLS-сертификата (allowInsecure)", "Disable TLS certificate verification (allowInsecure)"))).Append("</label>");
            sb.Append("<p class=\"hint warn\">").Append(E(T("Опасная настройка: подлинность сервера не проверяется. Включайте только осознанно для доверенного сервера с нестандартным сертификатом. Это не исправление сетевых ошибок; предпочтительно установить действительный сертификат.",
                "Risky setting: the server's identity is not checked. Enable only deliberately for a trusted server with a non-standard certificate. This is not a network-error fix; a valid certificate is preferable."))).Append("</p></details>");
        }
        sb.Append("<p class=hint>").Append(E(S("subs_kind_naive_hint", []))).Append("</p></div>");
        sb.Append("<div class=modal-actions>");
        sb.Append("<button type=submit>").Append(E(S(buttonKey, []))).Append("</button>");
        if (inModal)
            sb.Append("<button type=button class=ghost data-sub-close>").Append(E(S("btn_cancel", []))).Append("</button>");
        sb.Append("</div></form>");
    }

    private static string SubDialogId(string name) =>
        "subdlg-" + Math.Abs(StringComparer.Ordinal.GetHashCode(name)).ToString();

    internal static string ShortPath(string path)
    {
        var parts = path.TrimEnd('\\', '/').Split('\\', '/').Where(p => p.Length > 0).ToArray();
        if (parts.Length <= 3) return path;
        var sep = path.Contains('\\') ? "\\" : "/";
        return "…" + sep + string.Join(sep, parts[^2..]);
    }

    private static string HostOf(string url)
    {
        var at = url.LastIndexOf('@');
        var rest = at >= 0 ? "https://" + url[(at + 1)..] : url;
        return Uri.TryCreate(rest.Trim(), UriKind.Absolute, out var uri) ? uri.Host : "***";
    }

    public static string MaskUrl(string url)
    {
        if (NaiveProxyHelper.IsNaiveUri(url)) return MaskNaiveUrl(url);
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return "***";
        var tail = uri.PathAndQuery.TrimEnd('/');
        var visible = tail.Length > 8 ? tail[^4..] : "";
        return $"{uri.Scheme}://{uri.Authority}/***{visible}";
    }

    private static string MaskNaiveUrl(string url)
    {
        if (!NaiveProxyHelper.IsNaiveUri(url))
            return url;

        url = url.Trim();
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = url.IndexOfAny(['/', '?', '#'], schemeEnd);
        if (authorityEnd < 0) authorityEnd = url.Length;
        var at = url.LastIndexOf('@', authorityEnd - 1, authorityEnd - schemeEnd);
        if (at < 0) return url;
        return url[..schemeEnd] + "***:***" + url[at..];
    }

    private static readonly (LogView View, string Key, string Query)[] LogViews =
    {
        (LogView.Important, "log_view_important", "important"),
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

        var lines = Log.TailNewestFirst(200, view);
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
          .Append("<input type=hidden name=view value=").Append(current).Append('>')
          .Append("<button class=danger>").Append(E(S("log_clear", []))).Append("</button></form>");

        sb.Append("<form class=row method=post action=/log/level><input type=hidden name=tab value=log>")
          .Append("<input type=hidden name=view value=").Append(current).Append('>');
        sb.Append("<span style=\"align-self:center\">").Append(E(S("log_level", []))).Append(":</span>");
        sb.Append("<select name=level style=\"flex:0 0 160px\">");
        foreach (var level in new[] { "warn", "info", "debug", "error" })
            sb.Append("<option value=").Append(level)
              .Append(cfg.EngineLogLevel == level ? " selected" : "").Append('>')
              .Append(level).Append("</option>");
        sb.Append("</select><button class=ghost>").Append(E(S("btn_save", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("log_level_hint", []))).Append("</p></section>");
    }

    private static void RenderAccess(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S, string tab = "access")
    {
        sb.Append("<section><h2>").Append(E(S("nav_access", []))).Append("</h2>");

        var hasPassword = Auth.HasPassword(cfg);
        sb.Append("<p class=lede>")
          .Append(E(S(hasPassword ? "auth_hint_set" : "auth_no_password", []))).Append("</p>");
        sb.Append("<div class=\"status ").Append(hasPassword ? "on" : "bad").Append("\"><span class=dot></span><b>")
          .Append(E(S(hasPassword ? "auth_is_set" : "auth_not_set", []))).Append("</b></div>");

        sb.Append("<form class=row method=post action=/password><input type=hidden name=tab value=").Append(tab).Append(">");
        sb.Append("<input type=password name=password placeholder=\"").Append(E(S("auth_password", []))).Append("\">");
        sb.Append("<input type=password name=password2 placeholder=\"")
          .Append(E(S("setup_password_again", []))).Append("\">");
        sb.Append("<button>").Append(E(S("btn_save", []))).Append("</button></form>");
        if (Auth.HasPassword(cfg))
        {
            sb.Append("<form class=row method=post action=/password><input type=hidden name=tab value=").Append(tab).Append(">")
              .Append("<input type=hidden name=clear value=1>")
              .Append("<button class=danger>").Append(E(S("auth_remove", []))).Append("</button></form>");
        }
        sb.Append("</section>");

        sb.Append("<section><h2>").Append(E(S("transfer_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("transfer_lede", []))).Append("</p>");
        sb.Append("<h3>").Append(E(S("transfer_export", []))).Append("</h3>");
        sb.Append("<form class=row method=post action=/settings/export>");
        AppendTransferParts(sb, S);
        sb.Append("<input type=password name=password autocomplete=new-password minlength=6 required placeholder=\"")
          .Append(E(S("transfer_password", []))).Append("\">");
        sb.Append("<input type=password name=password2 autocomplete=new-password minlength=6 required placeholder=\"")
          .Append(E(S("setup_password_again", []))).Append("\">");
        sb.Append("<button>").Append(E(S("transfer_export_btn", []))).Append("</button></form>");
        sb.Append("<h3>").Append(E(S("transfer_import", []))).Append("</h3>");
        sb.Append("<form class=row method=post action=/settings/import id=settings-import><input type=hidden name=tab value=").Append(tab).Append(">");
        AppendTransferParts(sb, S);
        sb.Append("<label class=filepick><input type=file id=settings-file accept=\".chps,text/plain\" required>")
          .Append("<span class=fp-btn>").Append(E(S("file_choose", []))).Append("</span><span class=fp-name data-none=\"")
          .Append(E(S("file_none", []))).Append("\">").Append(E(S("file_none", []))).Append("</span></label>");
        sb.Append("<input type=hidden name=data id=settings-data>");
        sb.Append("<input type=password name=password autocomplete=off required placeholder=\"")
          .Append(E(S("transfer_password", []))).Append("\">");
        sb.Append("<button>").Append(E(S("transfer_import_btn", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("transfer_hint", []))).Append("</p>");
        sb.Append(WebUi.SettingsImportScript);
        sb.Append("</section>");

        sb.Append("<section><h2>").Append(E(S("lang_title", []))).Append("</h2>");
        sb.Append("<form class=row method=post action=/lang><input type=hidden name=tab value=").Append(tab).Append(">");
        sb.Append("<select name=lang style=\"flex:0 0 200px\">");
        foreach (var l in Strings.Languages)
            sb.Append("<option value=").Append(l).Append(cfg.Language == l ? " selected" : "").Append('>')
              .Append(l == "ru" ? "Русский" : "English").Append("</option>");
        sb.Append("</select><button>").Append(E(S("btn_save", []))).Append("</button></form></section>");

        sb.Append("<section><h2>").Append(E(S("uninstall_title", []))).Append("</h2>");
        sb.Append("<p class=hint>").Append(E(S("uninstall_hint", []))).Append("</p>");
        sb.Append("<form class=row method=post action=/uninstall onsubmit=\"return confirm('")
          .Append(E(S("uninstall_confirm_js", [])))
          .Append("');\"><input type=hidden name=tab value=").Append(tab).Append(">")
          .Append("<button class=danger>").Append(E(S("btn_uninstall", [])))
          .Append("</button></form></section>");
    }

    private static void AppendTransferParts(StringBuilder sb, Func<string, object[], string> S)
    {
        sb.Append("<div class=transfer-parts style=\"flex:1 0 100%\"><b>").Append(E(S("transfer_what", []))).Append(":</b> ");
        foreach (var (field, key) in new[] { ("p_vpn", "transfer_part_vpn"), ("p_apps", "transfer_part_apps"),
                     ("p_sites", "transfer_part_sites"), ("p_other", "transfer_part_other") })
            sb.Append("<label style=\"margin-right:14px;white-space:nowrap\"><input type=checkbox name=").Append(field)
              .Append(" value=1 checked> ").Append(E(S(key, []))).Append("</label>");
        sb.Append("</div>");
    }

    private static SettingsTransfer.Parts PartsFromForm(IReadOnlyDictionary<string, string> form)
    {
        var parts = SettingsTransfer.Parts.None;
        if (form.ContainsKey("p_vpn")) parts |= SettingsTransfer.Parts.Vpn;
        if (form.ContainsKey("p_apps")) parts |= SettingsTransfer.Parts.Apps;
        if (form.ContainsKey("p_sites")) parts |= SettingsTransfer.Parts.Sites;
        if (form.ContainsKey("p_other")) parts |= SettingsTransfer.Parts.Other;
        return parts;
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

        RenderSubscriptionHelp(sb, S);
        SafetyPanel.RenderReport(sb, cfg);

        // Про вмешательство в систему честнее рассказать самим, чем оставлять человека гадать,
        // почему в списке адаптеров появился ещё один туннель.
        sb.Append("<section><h2>").Append(E(S("touch_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("touch_lede", []))).Append("</p>");
        sb.Append("<ul class=steps>");
        foreach (var key in new[] { "touch_1", "touch_2", "touch_3" })
            sb.Append("<li>").Append(E(S(key, []))).Append("</li>");
        sb.Append("</ul>");
        sb.Append("<p class=hint>").Append(E(S("touch_not", []))).Append("</p></section>");
    }

    private const string VpnBot = "CeBers_VPN_bot";

    private static void RenderSubscriptionHelp(StringBuilder sb, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("sub_where_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("sub_where_what", []))).Append("</p>");
        sb.Append("<p class=hint>").Append(E(S("sub_where_formats", []))).Append("</p>");
        sb.Append("<p class=hint>").Append(E(S("sub_where_get", []))).Append("</p>");
        sb.Append("<p class=hint>").Append(E(S("sub_where_ours", []))).Append(' ')
          .Append("<a href=\"https://t.me/").Append(VpnBot).Append("\" target=_blank rel=noopener>@")
          .Append(VpnBot).Append("</a></p>");
        sb.Append("<p class=hint>").Append(E(S("sub_where_own", []))).Append("</p></section>");
    }

    private async Task RefreshLiveLatencyAsync(CehoConfig cfg)
    {
        if (Jobs.Active(JobRestore) is not null) return;
        if (DateTime.UtcNow - _liveLatencyAtUtc < TimeSpan.FromSeconds(20)) return;

        _liveLatency = await ClashLatency.ReadDelaysAsync(cfg.ClashApiPort);
        _liveLatencyAtUtc = DateTime.UtcNow;
    }
}

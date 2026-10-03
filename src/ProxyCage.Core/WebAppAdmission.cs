using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxyCage.Core;

public sealed partial class WebServer
{
    internal Func<string, IReadOnlyList<InstalledAppCatalog.Entry>>? InstalledApps { get; set; }
    /// <summary>Explicit removal of the last app also removes its admitted fail-closed scope.</summary>
    public Func<CehoConfig, Task<string?>>? OnRemoveLastApp { get; set; }
    /// <summary>Daemon callback enforces reconnect permission inside its engine queue.</summary>
    public Func<bool, IStageReport, Task<string>>? OnApplyWithRestartConfirmation { get; set; }

    private sealed record AppAddResult(string Message, bool IsError, string Status,
        string? AppId = null, string? Path = null)
    {
        public (string? Message, bool IsError, string? JobId) Legacy => (Message, IsError, null);
    }

    private static bool IsAppAddPath(string path) => path is "/apps/add" or "/apps/detected" or "/apps/installed";

    private AppAddResult AddAppCore(CehoConfig cfg, string route, Dictionary<string, string> form)
    {
        string T(string ru, string en) => cfg.Language == "ru" ? ru : en;
        string S(string key, params object[] args) => Strings.T(cfg.Language, key, args);
        try
        {
            var raw = form.GetValueOrDefault("path", "").Trim().Trim('"');
            if (raw.Length == 0) return new(S(route == "/apps/installed" ? "apps_installed_choose" : "err_need_path"), true, "error");
            if (raw.Length > 32768 || raw.Any(char.IsControl))
                return new(T("Некорректный путь программы.", "Invalid application path."), true, "error");
            InstalledAppCatalog.Entry? known = null;
            if (route == "/apps/installed")
            {
                known = (InstalledApps?.Invoke(cfg.Language) ?? InstalledAppCatalog.Detect(cfg.Language))
                    .FirstOrDefault(a => AppIdentity.SamePath(a.Path, raw));
                if (known is null) return new(S("apps_installed_missing"), true, "error");
                raw = known.Path;
            }
            var selected = AppIdentity.Normalize(raw);
            if (AppIdentity.Find(cfg.Apps, selected) is { } existing)
                return new(S("err_already_added"), false, "already_added", AppIdentity.Id(existing), AppIdentity.PathOf(existing));
            if (!File.Exists(raw) && !Directory.Exists(raw))
                return new(S("err_no_such_path", raw), true, "error");

            // Routing coverage is useful, but it is not proof that two applications are the same.
            if (AppCoverage.FindCoveringApp(cfg.Apps, selected) is { } covering)
                return new(T($"Путь уже покрывается правилом «{covering.Label}». Новая программа не добавлена.",
                    $"This path is already covered by the rule for {covering.Label}. No app was added."),
                    false, "covered", AppIdentity.Id(covering), AppIdentity.PathOf(covering));

            if (form.GetValueOrDefault("intent") == "tunnel" && form.GetValueOrDefault("confirm_add") != "1")
                return new(T("Подтвердите добавление программы в туннель.", "Confirm adding this app to the tunnel."), true, "error");
            if (Jobs.Active(JobRestore) is not null || Jobs.Active(JobApply) is not null || Jobs.Active(JobPower) is not null)
                return new(T("Дождитесь завершения текущего действия и повторите добавление.",
                    "Wait for the current operation to finish, then add the app again."), true, "error");

            var detected = AppDetector.Detect(selected, cfg.Language);
            // Detection may choose an ancestor installation folder. Reusing identical routing
            // coverage is still a coverage result, never an exact-identity duplicate.
            var sameRule = AppCoverage.FindEquivalentRule(cfg.Apps, detected);
            if (sameRule is { Enabled: false })
                return new(T($"Правило «{sameRule.Label}» для этого каталога уже сохранено, но отключено. Новая программа не добавлена.",
                    $"A saved rule for this folder ({sameRule.Label}) exists but is disabled. No app was added."),
                    false, "existing_rule", AppIdentity.Id(sameRule), AppIdentity.PathOf(sameRule));
            if (sameRule is not null)
                return new(T($"Правило «{sameRule.Label}» уже покрывает этот каталог. Новая программа не добавлена.",
                    $"The rule for {sameRule.Label} already covers this folder. No app was added."),
                    false, "covered", AppIdentity.Id(sameRule), AppIdentity.PathOf(sameRule));

            if (AppCoverage.FindStoredRule(cfg.Apps, detected) is { } storedRule)
                return new(T($"Программа «{storedRule.Label}» уже использует этот путь правила с другими настройками. Проверьте сохранённое правило. Новая программа не добавлена.",
                    $"A saved app ({storedRule.Label}) already uses this rule path with different settings. Review the existing rule. No app was added."),
                    false, "existing_rule", AppIdentity.Id(storedRule), AppIdentity.PathOf(storedRule));

            var app = new AppEntry
            {
                Name = known?.Name ?? (route == "/apps/detected" ? form.GetValueOrDefault("name", detected.Name) : detected.Name),
                Folder = detected.Folder, IdentityPath = selected,
                VersionAgnostic = detected.VersionAgnostic, SingleFile = detected.SingleFile,
                Launch = File.Exists(selected) ? selected : null,
            };
            cfg.Apps.Add(app);
            Save(cfg);
            Interlocked.Increment(ref _pending);
            InvalidateAppObservations();
            return new($"{S("added_name", app.Name)}. {detected.Explanation} " + T(
                "Правило сохранено. Нажмите «Применить», чтобы применить его; работающий туннель будет переподключён.",
                "Rule saved. Choose Apply to use it; a running tunnel will reconnect."),
                false, "added", AppIdentity.Id(app), selected);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new(ex.Message, true, "error");
        }
    }

    private async Task HandleAppAddPostAsync(HttpListenerContext ctx, string route, Dictionary<string, string> form)
    {
        AppAddResult result;
        await _configGate.WaitAsync();
        try { result = AddAppCore(CehoConfig.Load(_configPath), route, form); }
        finally { _configGate.Release(); }
        if (ctx.Request.AcceptTypes?.Any(a => a.Contains("application/json", StringComparison.OrdinalIgnoreCase)) == true)
        {
            await WriteObjectJsonAsync(ctx, new
            {
                ok = !result.IsError, duplicate = result.Status == "already_added", covered = result.Status == "covered",
                appId = result.AppId, path = result.Path,
                enabled = result.AppId is null ? (bool?)null : CehoConfig.Load(_configPath).Apps.FirstOrDefault(a => AppIdentity.Id(a) == result.AppId)?.Enabled,
                pending = Volatile.Read(ref _pending) > 0,
                applied = result.AppId is not null && CehoConfig.Load(_configPath).Apps.Any(a => AppIdentity.Id(a) == result.AppId && IsAppRuleApplied(a)),
                message = result.Message, status = result.Status,
            });
            return;
        }
        var q = "?tab=" + Uri.EscapeDataString(form.GetValueOrDefault("tab", "apps"))
            + "&m=" + Uri.EscapeDataString(result.Message) + "&e=" + (result.IsError ? "1" : "0");
        if (result.AppId is not null) q += "&app=" + result.AppId + "&app_result=" + result.Status;
        if (form.GetValueOrDefault("wizard") is { Length: > 0 } wizard) q += "&wizard=" + Uri.EscapeDataString(wizard);
        Redirect(ctx, "/" + q);
    }

    private Task HandleTunnelStateAsync(HttpListenerContext ctx, CehoConfig cfg) =>
        WriteObjectJsonAsync(ctx, TunnelState(cfg));

    internal object TunnelState(CehoConfig cfg)
    {
        var active = Jobs.Active(JobApply) ?? Jobs.Active(JobPower) ?? Jobs.Active(JobRestore);
        return new
        {
            running = _state().Running, pending = Volatile.Read(ref _pending) > 0, busy = active is not null,
            apps = cfg.Apps.Select(a => new { id = AppIdentity.Id(a), path = AppIdentity.PathOf(a), enabled = a.Enabled, ruleApplied = IsAppRuleApplied(a) }),
            job = active is null ? null : new { id = active.Id, state = active.State.ToString().ToLowerInvariant() },
        };
    }

    private static async Task WriteObjectJsonAsync(HttpListenerContext ctx, object data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private readonly object _appliedRulesGate = new();
    private string? _appliedConfigFingerprint;
    private HashSet<string> _appliedAppIds = new(StringComparer.Ordinal);

    private string? ConfigFingerprint()
    {
        // Load uses PrivateFile's bounded, non-symlink configuration read.
        try { return RoutingFingerprint(CehoConfig.Load(_configPath)); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    internal static string RoutingFingerprint(CehoConfig cfg)
    {
        // Include unknown future fields by default, excluding only known non-routing metadata.
        var data = JsonSerializer.SerializeToNode(cfg)!.AsObject();
        foreach (var key in new[] { "WebPort", "PanelMode", "Language", "PasswordHash", "PasswordSalt",
            "SetupDone", "AutostartOffered", "AutoUpdate", "TrayControls", "EngineAutoUpdate",
            "AutoUpdatedVersion", "AutoUpdatedAtUtc", "UpdateRepo" }) data.Remove(key);
        foreach (var app in data["Apps"]!.AsArray()) app!.AsObject().Remove("DisplayName");
        foreach (var sub in data["Subscriptions"]!.AsArray())
            foreach (var key in new[] { "LastCheckOk", "LastCheckedUtc", "ExpiresUtc", "UsedBytes", "TotalBytes", "LastNodes", "LastError" })
                sub!.AsObject().Remove(key);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }

    /// <summary>Called only after the engine has accepted these exact generated rules.</summary>
    public void NotifyRulesApplied(CehoConfig appliedConfig)
    {
        RecordAppliedRules(RoutingFingerprint(appliedConfig), Volatile.Read(ref _pending));
    }

    private void RecordAppliedRules(string? before, int pendingBefore)
    {
        var current = ConfigFingerprint();
        if (before is null || before != current) return;
        lock (_appliedRulesGate)
        {
            _appliedConfigFingerprint = current;
            _appliedAppIds = CehoConfig.Load(_configPath).Apps.Where(a => a.Enabled).Select(AppIdentity.Id).ToHashSet(StringComparer.Ordinal);
        }
        // A newer saved edit must not be cleared by completion of an older apply.
        Interlocked.CompareExchange(ref _pending, 0, pendingBefore);
    }

    private void ForgetAppliedRules()
    {
        lock (_appliedRulesGate) { _appliedConfigFingerprint = null; _appliedAppIds.Clear(); }
    }

    internal bool IsAppRuleApplied(AppEntry app)
    {
        if (!app.Enabled || !_state().Running || AppRulesPending) return false;
        var fingerprint = ConfigFingerprint();
        lock (_appliedRulesGate)
            return fingerprint is not null && fingerprint == _appliedConfigFingerprint && _appliedAppIds.Contains(AppIdentity.Id(app));
    }

    private static string ConfirmApplyMessage(CehoConfig cfg) => cfg.Language == "ru"
        ? "Подтвердите применение: работающий туннель переподключится, соединения выбранных программ могут кратко прерваться."
        : "Confirm Apply: the running tunnel will reconnect and selected apps may briefly lose connections.";
}

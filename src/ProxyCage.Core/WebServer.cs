using System.Net;
using System.Text;
using System.Web;

namespace ProxyCage.Core;

/// <summary>
/// Локальная панель управления на http://127.0.0.1:&lt;порт&gt;. Слушает только петлю.
///
/// Формы обычные POST + redirect: панель обязана работать, даже если что-то сломано,
/// поэтому не зависит от JavaScript. Разделы — обычные ссылки с параметром, без вкладок на скриптах.
/// </summary>
public sealed class WebServer
{
    private const string CookieName = "ceho";

    private readonly string _configPath;
    private readonly Func<ControlState> _state;
    private readonly Action<string> _log;
    private HttpListener? _listener;

    /// <param name="Probed">Проба выхода уже отработала хотя бы раз. Нужен, чтобы
    /// не красить состояние в «ошибка», пока идёт ПЕРВАЯ проверка: красный тут врёт —
    /// человек читает его как «сломано», хотя туннель только что поднялся.</param>
    public sealed record ControlState(
        bool Running, string? ExitCountry, string? ExitIp, string? LastError, bool Probed);

    public Func<Task<IReadOnlyList<NodeProbe.CountryRow>>>? OnCountries { get; set; }
    private IReadOnlyList<NodeProbe.CountryRow>? _countries;

    /// <summary>Ноды пула без замера. Страны надо показывать и при поднятом туннеле:
    /// иначе исключить страну нельзя, не выключив защиту, и человек попадает в тупик.</summary>
    public Func<Task<IReadOnlyList<ProxyNode>>>? OnPool { get; set; }

    public Func<Task<string?>>? OnStart { get; set; }
    public Func<Task<string?>>? OnStop { get; set; }
    public Func<Task<string?>>? OnApply { get; set; }

    /// <summary>Проверка и установка обновления. Делает демон: только у него есть права на файл.</summary>
    public Func<bool, Task<string>>? OnUpdate { get; set; }

    /// <summary>Проверка подписок настоящим запросом через туннель.</summary>
    public Func<Task<string>>? OnCheckSubs { get; set; }

    /// <summary>Команды, уже переведённые на туннель, — чтобы панель показывала их списком.</summary>
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
        try { _listener?.Stop(); } catch { /* закрываемся */ }
    }

    private async Task LoopAsync()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }

            try { await HandleAsync(ctx); }
            catch (Exception ex)
            {
                _log($"ошибка панели: {ex.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
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

        if (ctx.Request.HttpMethod == "POST")
        {
            var form = await ReadFormAsync(ctx.Request);
            var (msg, isError) = await ApplyPostAsync(path, form, cfg);
            var tab = form.GetValueOrDefault("tab", "state");
            var q = $"?tab={Uri.EscapeDataString(tab)}";
            if (msg is not null) q += $"&m={Uri.EscapeDataString(msg)}&e={(isError ? 1 : 0)}";
            Redirect(ctx, "/" + q);
            return;
        }

        var flash = ctx.Request.QueryString["m"];
        var flashErr = ctx.Request.QueryString["e"] == "1";
        var current = ctx.Request.QueryString["tab"] ?? "state";
        await WriteHtmlAsync(ctx, RenderPage(cfg, _state(), current, flash, flashErr));
    }

    // ── доступ ───────────────────────────────────────────────────────

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
                // HttpOnly — куку не должен читать скрипт; SameSite=Strict — не уедет по чужой ссылке
                ctx.Response.Headers.Add("Set-Cookie",
                    $"{CookieName}={token}; Path=/; HttpOnly; SameSite=Strict");
                Redirect(ctx, "/");
                return;
            }
            error = Strings.T(cfg.Language, "auth_wrong");
        }

        await WriteHtmlAsync(ctx, RenderGate(cfg, error));
    }

    /// <summary>
    /// Единая точка для терминала. Нужна для машин, где у человека нет прав на файл настроек
    /// (общий сервер) и нет браузера: команда уходит сюда, выполняется от имени службы,
    /// а обратно приходит ровно тот текст, который напечатал бы локальный CLI.
    /// </summary>
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

    // ── действия ─────────────────────────────────────────────────────

    private async Task<(string? Message, bool IsError)> ApplyPostAsync(
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
                    if (raw.Length == 0) return (S("err_need_path"), true);
                    if (!File.Exists(raw) && !Directory.Exists(raw))
                        return (S("err_no_such_path", raw), true);

                    var d = AppDetector.Detect(raw, cfg.Language);
                    if (cfg.Apps.Any(a => a.Folder.Equals(d.Folder, StringComparison.OrdinalIgnoreCase)))
                        return (S("err_already_added"), true);

                    cfg.Apps.Add(new AppEntry
                    {
                        Name = d.Name, Folder = d.Folder,
                        VersionAgnostic = d.VersionAgnostic,
                        SingleFile = d.SingleFile,
                        Launch = File.Exists(raw) ? raw : null,
                    });
                    Save(cfg);
                    var applied = OnApply is null ? null : await OnApply();
                    return ($"{S("added_name", d.Name)} {d.Explanation}" +
                            (applied is null ? "" : $" {applied}"), false);
                }

                case "/apps/detected":
                {
                    var raw = f.GetValueOrDefault("path", "").Trim();
                    if (raw.Length == 0) return (S("err_need_path"), true);

                    var d = AppDetector.Detect(raw, cfg.Language);
                    if (cfg.Apps.Any(a => a.Folder.Equals(d.Folder, StringComparison.OrdinalIgnoreCase)))
                        return (S("err_already_added"), true);

                    cfg.Apps.Add(new AppEntry
                    {
                        Name = f.GetValueOrDefault("name", d.Name),
                        Folder = d.Folder,
                        VersionAgnostic = d.VersionAgnostic,
                        SingleFile = d.SingleFile,
                    });
                    Save(cfg);
                    var applied = OnApply is null ? null : await OnApply();
                    return ($"{S("added_name", f.GetValueOrDefault("name", d.Name))} {d.Explanation}" +
                            (applied is null ? "" : $" {applied}"), false);
                }

                case "/apps/remove":
                {
                    var folder = f.GetValueOrDefault("folder", "");
                    cfg.Apps.RemoveAll(a => a.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
                    Save(cfg);
                    if (OnApply is not null) await OnApply();
                    return (S("removed"), false);
                }

                case "/subs/add":
                {
                    var url = f.GetValueOrDefault("url", "").Trim();
                    var name = f.GetValueOrDefault("name", "").Trim();
                    if (url.Length == 0) return (S("pf_no_subs_fix"), true);
                    if (name.Length == 0) name = $"sub{cfg.Subscriptions.Count + 1}";
                    if (cfg.Subscriptions.Any(s => s.Name == name))
                        return (S("err_sub_exists", name), true);

                    cfg.Subscriptions.Add(new SubscriptionEntry { Name = name, Url = url });
                    cfg.ActiveSubscription ??= name;
                    Save(cfg);
                    var applied = OnApply is null ? null : await OnApply();
                    return ($"{S("sub_added", name)}" + (applied is null ? "" : $" {applied}"), false);
                }

                case "/subs/check":
                {
                    if (OnCheckSubs is null) return ("no control", true);
                    if (!NodeProbe.TunnelIsUp(cfg.TunAddress)) return (S("sub_check_off"), true);
                    return (await OnCheckSubs(), false);
                }

                case "/subs/remove":
                {
                    var name = f.GetValueOrDefault("name", "");
                    cfg.Subscriptions.RemoveAll(s => s.Name == name);
                    if (cfg.ActiveSubscription == name)
                        cfg.ActiveSubscription = cfg.Subscriptions.FirstOrDefault()?.Name;
                    Save(cfg);
                    try
                    {
                        var cache = Path.Combine(Root, $"sub-{name}.txt");
                        if (File.Exists(cache)) File.Delete(cache);
                    }
                    catch { }
                    return (S("sub_removed", name), false);
                }

                case "/countries/save":
                {
                    // приходят только отмеченные — снятые вычисляем по полному списку
                    var all = (f.GetValueOrDefault("all", "") ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var on = f.Keys.Where(k => k.StartsWith("c_", StringComparison.Ordinal))
                        .Select(k => k[2..]).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var prevExcluded = new List<string>(cfg.ExcludedCountries);
                    var prevPreferred = new List<string>(cfg.PreferredCountries);

                    cfg.ExcludedCountries = all.Where(c => !on.Contains(c)).ToList();
                    cfg.PreferredCountries.RemoveAll(c => cfg.ExcludedCountries.Contains(c, StringComparer.OrdinalIgnoreCase));
                    Save(cfg);

                    // выбор, при котором в пуле не остаётся нод, сохранять нельзя:
                    // человек ушёл бы с настройкой, при которой туннель не поднимается
                    try
                    {
                        var applied = OnApply is null ? null : await OnApply();
                        return (applied ?? S("rules_rebuilt"), false);
                    }
                    catch (PoolEmptyException ex)
                    {
                        cfg.ExcludedCountries = prevExcluded;
                        cfg.PreferredCountries = prevPreferred;
                        Save(cfg);
                        return ($"{ex.Message} {S("change_reverted")}", true);
                    }
                    catch (Exception ex)
                    {
                        return (ex.Message, true);
                    }
                }

                case "/countries/refresh":
                {
                    if (OnCountries is null) return (S("measure_blocked"), true);
                    if (NodeProbe.TunnelIsUp(cfg.TunAddress)) return (S("measure_blocked"), true);
                    _countries = await OnCountries();

                    // замеры запоминаем: по ним потом отсеиваются медленные ноды,
                    // а мерить заново при каждой сборке правил — ждать на ровном месте.
                    // Но только настоящие: из-под чужого туннеля числа не про скорость нод
                    var items = _countries.SelectMany(c => c.Items).ToList();
                    if (NodeProbe.LooksLikeLocalAccept(items))
                        return (S("speed_local_accept"), true);

                    foreach (var m in items)
                        if (m.LatencyMs is { } ms) cfg.NodeLatency[m.Node.Key] = ms;
                    Save(cfg);
                    return (S("measure_done", _countries.Count), false);
                }

                case "/settings":
                {
                    cfg.RotationEnabled = f.ContainsKey("rotation");
                    var checkUrl = f.GetValueOrDefault("checkurl", "").Trim();
                    if (checkUrl.Length > 0) cfg.CheckUrl = checkUrl;

                    var prevLimit = cfg.MaxLatencyMs;
                    var speed = f.GetValueOrDefault("speed", "").Trim();
                    cfg.MaxLatencyMs = int.TryParse(speed, out var limit) && limit > 0 ? limit : null;
                    Save(cfg);

                    try
                    {
                        var appliedNow = OnApply is null ? null : await OnApply();
                        return (appliedNow ?? S("rules_rebuilt"), false);
                    }
                    catch (PoolEmptyException ex)
                    {
                        // откатываем только то, что пул и опустошило: прочие беды сборки
                        // правил (нет программ, нет подписки) к порогу отношения не имеют
                        cfg.MaxLatencyMs = prevLimit;
                        Save(cfg);
                        return ($"{ex.Message} {S("change_reverted")}", true);
                    }
                    catch (Exception ex)
                    {
                        return (ex.Message, true);
                    }
                }

                case "/lang":
                {
                    cfg.Language = Strings.Normalize(f.GetValueOrDefault("lang", "ru"));
                    Save(cfg);
                    return (Strings.T(cfg.Language, "lang_set", cfg.Language), false);
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
                        return (S("auth_cleared"), false);
                    }
                    if (pass.Length < 4) return (S("setup_password_short"), true);
                    if (pass != again) return (S("setup_password_mismatch"), true);
                    Auth.SetPassword(cfg, pass);
                    Save(cfg);
                    Auth.DropAllSessions();
                    return (S("auth_set_ok"), false);
                }

                case "/update":
                {
                    if (OnUpdate is null) return ("no control", true);
                    var install = f.ContainsKey("install");
                    try { return (await OnUpdate(install), false); }
                    catch (Exception ex)
                    {
                        // «проверить» и «обновить» — разные действия, и жалобы у них разные
                        return (S(install ? "upd_failed" : "upd_check_failed", ex.Message), true);
                    }
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
                        ? (S(on ? "autostart_state_on" : "autostart_state_off"), false)
                        : (err, true);
                }

                case "/control/start":
                {
                    var err = OnStart is null ? "no control" : await OnStart();
                    return err is null ? (S("state_on"), false) : (err, true);
                }

                case "/control/stop":
                {
                    var err = OnStop is null ? "no control" : await OnStop();
                    return err is null ? (S("state_off"), false) : (err, true);
                }
            }
            return (null, false);
        }
        catch (Exception ex)
        {
            return (ex.Message, true);
        }
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

    // ── страница входа ───────────────────────────────────────────────

    private static string RenderGate(CehoConfig cfg, string? error)
    {
        var sb = new StringBuilder();
        Head(sb, cfg);
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

    private static void Head(StringBuilder sb, CehoConfig cfg)
    {
        sb.Append("<!doctype html><html lang=").Append(cfg.Language).Append("><head><meta charset=utf-8>");
        sb.Append("<meta name=viewport content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>CehoProxy</title><style>").Append(WebUi.Css).Append("</style></head><body>");
    }

    // ── основная страница ────────────────────────────────────────────

    private string RenderPage(CehoConfig cfg, ControlState st, string tab, string? flash, bool flashErr)
    {
        string S(string key, params object[] a) => Strings.T(cfg.Language, key, a);
        var sb = new StringBuilder();
        Head(sb, cfg);
        sb.Append("<div class=wrap>");

        sb.Append("<header>");
        sb.Append("<img class=logo src=\"").Append(Brand.LogoDataUri).Append("\" alt=\"КодоЦех\">");
        sb.Append("<span class=mark>Ceho<span>Proxy</span></span>");
        sb.Append("<span class=where>").Append(E($"127.0.0.1:{cfg.WebPort}")).Append("</span></header>");

        var tabs = new (string Id, string Key)[]
        {
            ("state", "nav_state"), ("apps", "nav_apps"), ("subs", "nav_subs"),
            ("exit", "nav_exit"), ("browser", "nav_browser"), ("access", "nav_access"), ("help", "nav_help"),
        };
        sb.Append("<nav class=tabs>");
        foreach (var (id, key) in tabs)
            sb.Append("<a href=\"/?tab=").Append(id).Append('"')
              .Append(id == tab ? " class=on" : "").Append('>').Append(E(S(key))).Append("</a>");
        sb.Append("</nav>");

        if (!string.IsNullOrEmpty(flash))
            sb.Append("<div class=\"flash ").Append(flashErr ? "err" : "ok").Append("\">")
              .Append(E(flash)).Append("</div>");

        switch (tab)
        {
            case "apps": RenderApps(sb, cfg, S); break;
            case "subs": RenderSubs(sb, cfg, S); break;
            case "exit": RenderExit(sb, cfg, S); break;
            case "browser": RenderBrowser(sb, cfg, S); break;
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
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private void RenderState(StringBuilder sb, CehoConfig cfg, ControlState st, Func<string, object[], string> S)
    {
        sb.Append("<section>");
        var cls = !st.Running ? "off"
                : st.ExitIp is not null ? "on"
                : st.Probed ? "bad"          // проба отработала и не нашла выхода — это правда ошибка
                : "wait";                    // первая проверка ещё идёт, пугать красным нельзя
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

        if (st.LastError is not null)
            sb.Append("<div class=\"flash err\">").Append(E(st.LastError)).Append("</div>");

        sb.Append("<form class=row method=post action=\"")
          .Append(st.Running ? "/control/stop" : "/control/start").Append("\">")
          .Append("<input type=hidden name=tab value=state>")
          .Append("<button>").Append(E(st.Running ? S("btn_off", []) : S("btn_on", [])))
          .Append("</button></form>");

        var checks = Preflight.Run(cfg, Root);
        var problems = checks.Where(c => c.Level != Preflight.Level.Ok)
            .Where(c => !c.Title.Contains("орт ", StringComparison.OrdinalIgnoreCase)
                     && !c.Title.Contains("ort ", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var c in problems)
        {
            sb.Append("<div class=\"flash").Append(c.Level == Preflight.Level.Blocker ? " err" : "").Append("\"><b>")
              .Append(E(c.Title)).Append("</b>");
            if (c.Detail is not null) sb.Append(E(c.Detail)).Append("<br>");
            if (c.Fix is not null) sb.Append(E(c.Fix));
            sb.Append("</div>");
        }
        sb.Append("</section>");

        sb.Append("<section><h2>").Append(E(S("summary_title", []))).Append("</h2><dl class=kv>");
        sb.Append("<dt>").Append(E(S("nav_apps", []))).Append("</dt><dd>")
          .Append(cfg.Apps.Count(a => a.Enabled)).Append("</dd>");
        sb.Append("<dt>").Append(E(S("nav_subs", []))).Append("</dt><dd>")
          .Append(cfg.Subscriptions.Count).Append("</dd>");
        sb.Append("<dt>").Append(E(S("nav_exit", []))).Append("</dt><dd>")
          .Append(E(cfg.PreferredCountries.Count > 0
              ? string.Join(", ", cfg.PreferredCountries)
              : S("country_any", []) + (cfg.ExcludedCountries.Count > 0
                  ? " (\u2212" + string.Join(", ", cfg.ExcludedCountries) + ")" : "")))
          .Append("</dd>");
        sb.Append("<dt>").Append(E(S("nav_browser", []))).Append("</dt><dd>127.0.0.1:")
          .Append(cfg.MixedPort).Append("</dd>");
        sb.Append("</dl></section>");

        sb.Append("<section><h2>").Append(E(S("upd_title", []))).Append("</h2>");
        sb.Append("<p class=hint>").Append(E(S("upd_current", new object[] { Updater.CurrentVersion })))
          .Append("</p>");
        sb.Append("<form class=row method=post action=/update><input type=hidden name=tab value=state>")
          .Append("<button class=ghost>").Append(E(S("upd_check", []))).Append("</button></form>");
        sb.Append("<form class=row method=post action=/update><input type=hidden name=tab value=state>")
          .Append("<input type=hidden name=install value=1>")
          .Append("<button>").Append(E(S("upd_apply", []))).Append("</button></form></section>");

        sb.Append("<section><h2>").Append(E(S("autostart_title", []))).Append("</h2>");
        var auto = Autostart.IsEnabled();
        sb.Append("<div class=\"status ").Append(auto ? "on" : "off").Append("\"><span class=dot></span><b>")
          .Append(E(auto ? S("autostart_on", []) : S("autostart_off", []))).Append("</b></div>");
        sb.Append("<form class=row method=post action=/autostart><input type=hidden name=tab value=state>");
        if (!auto) sb.Append("<input type=hidden name=enable value=1>");
        sb.Append("<button class=ghost>").Append(E(auto ? S("autostart_del", []) : S("autostart_add", [])))
          .Append("</button></form></section>");
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

    /// <summary>
    /// Найденные ИИ-инструменты. Скриптовые показываем тоже, но честно объясняем, что правило
    /// по файлу не сработает: молчаливая «изоляция», которой нет, хуже отсутствия кнопки.
    /// </summary>
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

        // пример строим по найденному инструменту, а не шаблоном: команду из панели
        // человек копирует целиком, и «<команда>» в ней выполниться не может
        var sample = found.FirstOrDefault(t => t.Kind == AiTools.ToolKind.Script)?.Path;
        var name = sample is not null ? Path.GetFileNameWithoutExtension(sample) : "gemini";
        sb.Append("<dl class=kv>");
        sb.Append("<dt>").Append(E(S("run_once", []))).Append("</dt><dd>chp run ").Append(E(name)).Append("</dd>");
        sb.Append("<dt>").Append(E(S("run_always", []))).Append("</dt><dd>chp wrap ").Append(E(name)).Append("</dd>");
        sb.Append("<dt>").Append(E(S("run_undo", []))).Append("</dt><dd>chp unwrap ").Append(E(name)).Append("</dd>");
        sb.Append("</dl>");

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

    private static void RenderSubs(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("nav_subs", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("subs_pool", []))).Append("</p>");

        if (cfg.Subscriptions.Count == 0)
            sb.Append("<p class=empty>").Append(E(S("subs_empty", []))).Append("</p>");
        else
        {
            sb.Append("<table><tr><th>").Append(E(S("col_name", []))).Append("</th><th>")
              .Append(E(S("col_link", []))).Append("</th><th>").Append(E(S("col_state", [])))
              .Append("</th><th></th></tr>");
            foreach (var s in cfg.Subscriptions)
            {
                sb.Append("<tr><td>").Append(E(s.Name)).Append("</td><td class=path>").Append(E(s.Url))
                  .Append("</td><td>").Append(E(s.LastCheckOk switch
                  {
                      true => S("sub_ok", []),
                      false => S("sub_bad", []),
                      null => S("sub_unchecked", []),
                  }));
                if (s.LastCheckedUtc is not null && DateTime.TryParse(s.LastCheckedUtc,
                        null, System.Globalization.DateTimeStyles.RoundtripKind, out var when))
                    sb.Append("<br><span class=tag>")
                      .Append(E(S("sub_checked_at", new object[] { when.ToLocalTime().ToString("dd.MM HH:mm") })))
                      .Append("</span>");
                sb.Append("</td><td class=actions>");
                sb.Append("<form method=post action=/subs/remove><input type=hidden name=tab value=subs>")
                  .Append("<input type=hidden name=name value=\"").Append(E(s.Name))
                  .Append("\"><button class=danger>").Append(E(S("btn_delete", []))).Append("</button></form>");
                sb.Append("</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("<form class=row method=post action=/subs/check><input type=hidden name=tab value=subs>")
          .Append("<button class=ghost>").Append(E(S("sub_check", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("sub_checking", []))).Append("</p>");

        sb.Append("<form class=row method=post action=/subs/add><input type=hidden name=tab value=subs>");
        sb.Append("<input type=text name=name placeholder=\"").Append(E(S("col_name", [])))
          .Append("\" style=\"flex:0 0 180px;min-width:130px\">");
        sb.Append("<input type=text name=url placeholder=\"https://…\">");
        sb.Append("<button>").Append(E(S("btn_add", []))).Append("</button></form></section>");
    }

    private void RenderExit(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("countries_title", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("countries_hint", []))).Append("</p>");

        // страны берём из пула, а не из результатов замера: замер при поднятом туннеле
        // запрещён, и иначе раздел был бы пустым ровно тогда, когда он нужен
        IReadOnlyList<ProxyNode> pool = Array.Empty<ProxyNode>();
        string? poolError = null;
        try { if (OnPool is not null) pool = OnPool().GetAwaiter().GetResult(); }
        catch (Exception ex) { poolError = ex.Message; }

        if (poolError is not null)
            sb.Append("<div class=\"flash err\">").Append(E(poolError)).Append("</div>");

        // группу «страна не определена» показываем наравне с остальными: её ноды
        // работают и лежат в пуле, поэтому человек должен их видеть и уметь выключить
        var groups = pool.GroupBy(n => n.CountryCode ?? CountryResolver.Unknown)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (groups.Count == 0)
            sb.Append("<p class=empty>").Append(E(S("pf_no_subs_detail", []))).Append("</p>");
        else
        {
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
        }

        sb.Append("<form class=row method=post action=/countries/refresh><input type=hidden name=tab value=exit>")
          .Append("<button class=ghost>").Append(E(S("btn_measure", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("udp_not_measured", []))).Append("</p>");

        sb.Append("<form class=row method=post action=/settings><input type=hidden name=tab value=exit>");
        sb.Append("<label class=check><input type=checkbox name=rotation")
          .Append(cfg.RotationEnabled ? " checked" : "").Append("> ")
          .Append(E(S("rotation_label", []))).Append("</label>");
        sb.Append("<input type=text name=checkurl value=\"").Append(E(cfg.CheckUrl)).Append("\">");
        sb.Append("<label class=field><span>").Append(E(S("speed_limit", []))).Append("</span>")
          .Append("<input type=text name=speed inputmode=numeric value=\"")
          .Append(cfg.MaxLatencyMs?.ToString() ?? "").Append("\" placeholder=\"500\"></label>");
        sb.Append("<button class=ghost>").Append(E(S("btn_save_check", []))).Append("</button></form>");
        sb.Append("<p class=hint>").Append(E(S("checkurl_hint", []))).Append("</p>");
        sb.Append("<p class=hint>").Append(E(S("speed_unmeasured_note", []))).Append("</p></section>");
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

    private static void RenderAccess(StringBuilder sb, CehoConfig cfg, Func<string, object[], string> S)
    {
        sb.Append("<section><h2>").Append(E(S("nav_access", []))).Append("</h2>");
        sb.Append("<p class=lede>").Append(E(S("auth_hint", []))).Append("</p>");
        var hasPassword = Auth.HasPassword(cfg);
        sb.Append("<div class=\"status ").Append(hasPassword ? "on" : "bad").Append("\"><span class=dot></span><b>")
          .Append(E(S(hasPassword ? "auth_is_set" : "auth_not_set", []))).Append("</b></div>");
        if (!hasPassword)
            sb.Append("<p class=hint>").Append(E(S("auth_no_password", []))).Append("</p>");

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
        sb.Append("<p class=hint>").Append(E(S("help_multiuser", []))).Append("</p></section>");
    }
}

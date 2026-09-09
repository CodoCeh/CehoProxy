using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using ProxyCage.Core;

namespace ProxyCage.Cli;

public static class Ceho
{
    public static bool Quiet { get; set; }

    public static string Root =>
        Environment.GetEnvironmentVariable("CEHOPROXY_HOME") ?? Os.DefaultRoot;

    public static string ConfigPath => Path.Combine(Root, "config.json");
    public static string RuntimeConfigPath => Path.Combine(Root, "singbox.json");

    public static string SingBoxPath =>
        Os.ResolveSingBox(Root) ?? Path.Combine(Root, Os.EngineFileName);

    private static string SubCachePath(string name) => Path.Combine(Root, $"sub-{name}.txt");

    public static string OwnExecutablePath
    {
        get
        {
            var installed = Path.Combine(Root, Os.IsWindows ? "cehoproxy.exe" : "cehoproxy");
            if (File.Exists(installed)) return installed;
            return Environment.ProcessPath ?? (Os.IsWindows ? "cehoproxy.exe" : "/usr/local/bin/cehoproxy");
        }
    }

    private enum FetchPersona { Client, Clash, Browser }

    private const int FetchAttempts = 5;
    private const int MinSubscriptionTimeoutSeconds = 45;

    private static int SubscriptionTimeout(int timeoutSeconds) =>
        Math.Max(MinSubscriptionTimeoutSeconds, timeoutSeconds);

    private static HttpClient MakeClient(
        string? proxy = null, FetchPersona persona = FetchPersona.Client, int timeoutSeconds = 45)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };
        if (proxy is null)
        {
            // Windows system proxy (e.g. stale 127.0.0.1:2080) must not hijack subscription fetches.
            handler.UseProxy = false;
            handler.ConnectCallback = ConnectBypassingSystemDnsAsync;
        }
        else
        {
            handler.Proxy = new WebProxyStub(proxy);
            handler.UseProxy = true;
        }
        var c = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)),
        };
        switch (persona)
        {
            case FetchPersona.Browser:
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");
                c.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                break;
            case FetchPersona.Clash:
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "clash-meta/1.18.0");
                c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
                break;
            default:
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "CehoProxy/" + Updater.CurrentVersion);
                c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/plain,*/*");
                break;
        }

        var hwid = DeviceStamp.LoadOrCreate(Root);
        c.DefaultRequestHeaders.TryAddWithoutValidation("x-hwid", hwid);
        c.DefaultRequestHeaders.TryAddWithoutValidation("x-device-os",
            Os.IsWindows ? "Windows" : Os.IsMac ? "macOS" : "Linux");
        c.DefaultRequestHeaders.TryAddWithoutValidation("x-ver-os", Environment.OSVersion.Version.ToString());
        c.DefaultRequestHeaders.TryAddWithoutValidation("x-device-model", "CehoProxy");
        return c;
    }

    private static bool LooksLikeWebPage(string body)
    {
        var head = body.TrimStart();
        return head.StartsWith('<')
            || head.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase);
    }

    private static string TunAddressForBypass()
    {
        try { return CehoConfig.Load(ConfigPath).TunAddress; }
        catch { return CehoConfig.DefaultTunAddress; }
    }

    private static async ValueTask<Stream> ConnectBypassingSystemDnsAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var tunAddress = TunAddressForBypass();
        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await DirectDnsResolver.ResolveAsync(host, cancellationToken, tunAddress);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var bind = Os.PhysicalBindAddress(tunAddress);
        if (bind is not null)
            socket.Bind(new IPEndPoint(bind, 0));
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private sealed class WebProxyStub : IWebProxy
    {
        private readonly Uri _uri;
        public WebProxyStub(string uri) => _uri = new Uri(uri);
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) => _uri;
        public bool IsBypassed(Uri host) => host.IsLoopback;
    }

    private static async Task<IReadOnlyList<ProxyNode>> LoadOneAsync(
        SubscriptionEntry sub, string lang, bool preferCache = false,
        IStageReport? report = null, int timeoutSeconds = 15)
    {
        if (ReadWithoutNetwork(sub.Url, lang) is { Count: > 0 } local)
        {
            Mark(sub, true, local.Count, null, ExpiryFrom(null, local));
            return Tag(local, sub.Name);
        }

        var cache = SubCachePath(sub.Name);

        if (preferCache && File.Exists(cache))
        {
            var saved = SubscriptionParser.Parse(await File.ReadAllTextAsync(cache), lang);
            if (saved.Count > 0) return Tag(saved, sub.Name);
        }

        var fetched = await FetchWithRetriesAsync(sub.Url, report, lang, timeoutSeconds);
        if (fetched.Body is not null)
        {
            report?.Note(Strings.T(lang, "sub_parsing_nodes"));
            var freshNodes = SubscriptionParser.Parse(fetched.Body, lang);
            if (freshNodes.Count > 0)
            {
                Directory.CreateDirectory(Root);
                await File.WriteAllTextAsync(cache, fetched.Body);
                Mark(sub, true, freshNodes.Count, null, ExpiryFrom(fetched.UserInfo, freshNodes));
                return Tag(freshNodes, sub.Name);
            }
            Log.Warn($"подписка «{sub.Name}» ответила, но нод в ответе нет");
            if (!Quiet) Console.Error.WriteLine($"подписка «{sub.Name}» ответила, но нод в ответе нет");
            if (SubscriptionParser.LooksLikeHwidGate(fetched.Body))
                fetched = fetched with { Failure = Strings.T(lang, "sub_hwid_gate") };
        }
        else
        {
            Log.Warn($"подписка «{sub.Name}» не скачалась: {fetched.Failure}");
            if (!Quiet) Console.Error.WriteLine($"подписка «{sub.Name}» не скачалась: {fetched.Failure}");
        }

        if (File.Exists(cache))
        {
            var cached = SubscriptionParser.Parse(await File.ReadAllTextAsync(cache), lang);
            if (cached.Count > 0)
            {
                Log.Info($"использую сохранённую копию подписки «{sub.Name}»");
                Mark(sub, false, cached.Count, fetched.Failure, null);
                return Tag(cached, sub.Name);
            }
        }

        Mark(sub, false, 0, fetched.Failure, null);
        return Array.Empty<ProxyNode>();
    }

    private static SubscriptionInfo? ExpiryFrom(string? userInfo, IReadOnlyList<ProxyNode> nodes)
    {
        var fromHeader = SubscriptionInfo.FromHeader(userInfo);
        if (fromHeader is not null) return fromHeader;

        var fromRemarks = SubscriptionInfo.ExpiryFromRemarks(nodes.Select(n => n.Remark ?? ""));
        return fromRemarks is null ? null : new SubscriptionInfo(fromRemarks, null, null, null);
    }

    private readonly record struct Fetched(string? Body, string? Failure, string? UserInfo);

    private static FetchPersona PersonaOf(int attempt) => attempt switch
    {
        1 => FetchPersona.Client,
        2 => FetchPersona.Clash,
        3 => FetchPersona.Browser,
        _ => FetchPersona.Client,
    };

    private static async Task<(string Body, long? ExpectedLength)> ReadBodyAsync(HttpResponseMessage response)
    {
        var expected = response.Content.Headers.ContentLength;
        var body = await response.Content.ReadAsStringAsync();
        return (body, expected);
    }

    private static bool IsIncomplete(long? expectedLength, int receivedLength) =>
        expectedLength is > 0 && receivedLength < expectedLength.Value;

    private static async Task<Fetched> FetchWithRetriesAsync(
        string url, IStageReport? report = null, string lang = "ru", int timeoutSeconds = 45)
    {
        var fetchTimeout = SubscriptionTimeout(timeoutSeconds);
        string? failure = null;
        string? webPage = null;
        for (var attempt = 1; attempt <= FetchAttempts; attempt++)
        {
            report?.Note(Strings.T(lang, "sub_fetch_attempt", attempt, FetchAttempts));
            try
            {
                using var http = MakeClient(null, PersonaOf(attempt), fetchTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = HttpVersion.Version11,
                };
                using var response = await http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead);
                var userInfo = UserInfoHeader(response);
                if (response.IsSuccessStatusCode)
                {
                    report?.Note(Strings.T(lang, "sub_reading_data"));
                    var (body, expectedLength) = await ReadBodyAsync(response);
                    if (body.Trim().Length == 0)
                    {
                        failure = $"пустой ответ (попытка {attempt})";
                        report?.Note(failure);
                    }
                    else if (IsIncomplete(expectedLength, body.Length))
                    {
                        failure = Strings.T(lang, "sub_incomplete", body.Length, expectedLength!.Value);
                        report?.Note(failure);
                    }
                    else if (LooksLikeWebPage(body))
                    {
                        webPage ??= body;
                    }
                    else if (SubscriptionParser.LooksLikeHwidGate(body)
                             || HeaderTrue(response, "x-hwid-max-devices-reached"))
                    {
                        failure = Strings.T(lang, HeaderTrue(response, "x-hwid-max-devices-reached")
                            ? "sub_hwid_limit"
                            : "sub_hwid_gate");
                        report?.Note(failure);
                    }
                    else
                    {
                        var kb = Math.Max(1, body.Length / 1024);
                        report?.Note(Strings.T(lang, "sub_parsing", kb));
                        return new Fetched(body, null, userInfo);
                    }
                }
                else
                {
                    failure = $"HTTP {(int)response.StatusCode}";
                    if (attempt < FetchAttempts)
                        report?.Note(Strings.T(lang, "sub_fetch_retry", attempt, failure));
                }
            }
            catch (Exception ex)
            {
                var msg = ex is TaskCanceledException ? Strings.T(lang, "sub_timeout") : ex.Message;
                failure = msg;
                if (attempt < FetchAttempts)
                    report?.Note(Strings.T(lang, "sub_fetch_retry", attempt, failure));
            }

            if (attempt < FetchAttempts)
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, attempt * 2)));
        }

        return webPage is not null && failure is null
            ? new Fetched(webPage, null, null)
            : new Fetched(null, failure, null);
    }

    private static bool HeaderTrue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
        && values.Any(v => v.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static string? UserInfoHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues(SubscriptionInfo.HeaderName, out var values)
        || response.Content.Headers.TryGetValues(SubscriptionInfo.HeaderName, out values)
            ? values.FirstOrDefault()
            : null;

    private static IReadOnlyList<ProxyNode>? ReadWithoutNetwork(string source, string lang)
    {
        var text = source.Trim();
        if (SubscriptionParser.LooksLikeNodeUri(text))
            return SubscriptionParser.Parse(text, lang);

        try
        {
            if (File.Exists(text)) return SubscriptionParser.Parse(File.ReadAllText(text), lang);
        }
        catch { }

        return null;
    }

    private static void Mark(
        SubscriptionEntry sub, bool ok, int nodes, string? error, SubscriptionInfo? info)
    {
        try
        {
            var cfg = CehoConfig.Load(ConfigPath);
            var entry = cfg.Subscriptions.FirstOrDefault(s => s.Name == sub.Name);
            if (entry is null) return;

            entry.LastCheckOk = ok;
            entry.LastCheckedUtc = DateTime.UtcNow.ToString("u");
            entry.LastNodes = nodes;
            entry.LastError = ok ? null : error;

            // Годный ответ обновляет срок и трафик; неудачная попытка прежние цифры не стирает.
            if (info is not null)
            {
                if (info.ExpiresUtc is not null) entry.ExpiresUtc = info.ExpiresUtc;
                if (info.UsedBytes is not null) entry.UsedBytes = info.UsedBytes;
                if (info.TotalBytes is not null) entry.TotalBytes = info.TotalBytes;
            }

            cfg.Save(ConfigPath);
        }
        catch { }
    }

    private static IReadOnlyList<ProxyNode> Tag(IReadOnlyList<ProxyNode> nodes, string source)
    {
        foreach (var n in nodes) n.Source = source;
        return nodes;
    }

    public static async Task<IReadOnlyList<ProxyNode>> LoadAllNodesAsync(
        CehoConfig cfg, bool preferCache = false, IStageReport? report = null)
    {
        if (cfg.Subscriptions.Count == 0)
            throw new InvalidOperationException(Strings.T(cfg.Language, "pf_no_subs"));

        var active = cfg.Subscriptions.Where(s => s.Enabled).ToList();
        if (active.Count == 0)
            throw new PoolEmptyException(Strings.T(cfg.Language, "subs_all_off"));

        var done = 0;
        var lists = active.Count == 0
            ? Array.Empty<IReadOnlyList<ProxyNode>>()
            : await Task.WhenAll(active.Select(async s =>
            {
                var nodes = await LoadOneAsync(s, cfg.Language, preferCache, report, cfg.TimeoutSeconds);
                var ready = Interlocked.Increment(ref done);
                report?.Stage(
                    Strings.T(cfg.Language, "sub_progress", s.Name, nodes.Count, ready, active.Count),
                    active.Count > 0 ? ready * 90 / active.Count : 90);
                return nodes;
            }));

        report?.Stage(Strings.T(cfg.Language, "sub_building_pool"), 95);

        var pool = new List<ProxyNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var node in lists.SelectMany(l => l).Where(n => !n.IsMeta))
        {
            var key = $"{node.Protocol}|{node.Server}|{node.Port}|{node.Credential}";
            if (!seen.Add(key)) continue;
            node.Tag = $"n{++index:D3}";
            pool.Add(node);
        }

        if (pool.Count == 0)
            throw new PoolEmptyException(Strings.T(cfg.Language, "pool_empty"));

        return pool;
    }

    public static async Task<string> ApplyAsync(IStageReport? report = null)
    {
        var cfg = CehoConfig.Load(ConfigPath);
        var nodes = await LoadAllNodesAsync(cfg, preferCache: false, report);
        report?.Stage(Strings.T(cfg.Language, "stage_writing_rules"), 97);
        var json = SingBoxConfigGenerator.GenerateForConfig(nodes, cfg);
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(RuntimeConfigPath, json);
        return Strings.T(cfg.Language, "rules_rebuilt");
    }

    public static Task<(string? Country, string? Ip)> ProbeExitAsync(int mixedPort) => Task.Run(() =>
    {
        var curl = Os.ResolveCurl();
        if (curl is null) return (null, null);

        var (code, output) = Os.Run(curl,
            $"-s --max-time 15 -x socks5h://127.0.0.1:{mixedPort} http://ip-api.com/json", 20000);
        if (code != 0) return (null, null);

        return (Extract(output, "countryCode"), Extract(output, "query"));
    });

    private static async Task<bool> CheckSubscriptionLiveAsync(int mixedPort, string checkUrl, int timeoutSeconds = 15)
    {
        try
        {
            using var http = MakeClient($"http://127.0.0.1:{mixedPort}", FetchPersona.Client, timeoutSeconds);
            using var resp = await http.GetAsync(checkUrl);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static async Task<string?> RefreshIfDeadAsync(int mixedPort)
    {
        var cfg = CehoConfig.Load(ConfigPath);
        if (!cfg.RotationEnabled || cfg.Subscriptions.Count == 0) return null;

        if (await CheckSubscriptionLiveAsync(mixedPort, cfg.CheckUrl, cfg.TimeoutSeconds)) return null;

        var before = SubscriptionsFingerprint();
        try
        {
            var nodes = await LoadAllNodesAsync(cfg);
            await File.WriteAllTextAsync(RuntimeConfigPath,
                SingBoxConfigGenerator.GenerateForConfig(nodes, cfg));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        return SubscriptionsFingerprint() != before
            ? "подписки обновились, правила перечитаны"
            : "ни одна нода не отвечает";
    }

    private static string SubscriptionsFingerprint()
    {
        try
        {
            var parts = Directory.GetFiles(Root, "sub-*.txt")
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}:{File.GetLastWriteTimeUtc(f):O}");
            return string.Join("|", parts);
        }
        catch
        {
            return "";
        }
    }

    public static async Task<string> DiagnoseSubscriptionAsync(string url, string lang, int timeoutSeconds = 45)
    {
        var text = url.Trim();

        if (SubscriptionParser.LooksLikeNodeUri(text))
            return Strings.T(lang, "diag_node_uri_bad");

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return File.Exists(text)
                ? Strings.T(lang, "diag_file_bad")
                : Strings.T(lang, "diag_not_a_link");

        var fetchTimeout = SubscriptionTimeout(timeoutSeconds);
        try
        {
            string? failure = null;
            for (var attempt = 1; attempt <= FetchAttempts; attempt++)
            {
                using var http = MakeClient(null, PersonaOf(attempt), fetchTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Version = HttpVersion.Version11,
                };
                using var response = await http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead);
                var code = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    failure = Strings.T(lang, code >= 500 ? "diag_server_down" : "diag_http_error", code);
                    if (attempt < FetchAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, attempt * 2)));
                        continue;
                    }
                    return failure;
                }

                var (body, expectedLength) = await ReadBodyAsync(response);
                if (body.Trim().Length == 0) return Strings.T(lang, "diag_empty");
                if (IsIncomplete(expectedLength, body.Length))
                {
                    failure = Strings.T(lang, "sub_incomplete", body.Length, expectedLength!.Value);
                    if (attempt < FetchAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, attempt * 2)));
                        continue;
                    }
                    return failure;
                }

                if (SubscriptionParser.LooksLikeHwidGate(body)
                    || HeaderTrue(response, "x-hwid-max-devices-reached"))
                {
                    return Strings.T(lang, HeaderTrue(response, "x-hwid-max-devices-reached")
                        ? "sub_hwid_limit"
                        : "sub_hwid_gate");
                }

                var parsed = SubscriptionParser.Parse(body, lang);
                if (parsed.Count > 0)
                    return Strings.T(lang, "sub_parsed", parsed.Count);

                var looksHtml = LooksLikeWebPage(body);
                return Strings.T(lang, looksHtml ? "diag_html" : "diag_unknown_format",
                    body.Trim().Length);
            }

            return failure ?? Strings.T(lang, "diag_no_answer", Strings.T(lang, "sub_timeout"));
        }
        catch (TaskCanceledException)
        {
            return Strings.T(lang, "diag_timeout");
        }
        catch (HttpRequestException ex)
        {
            return Strings.T(lang, "diag_no_answer", ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return Strings.T(lang, "diag_no_answer", ex.Message);
        }
    }

    public sealed record VerifyResult(
        bool? Isolated, int Processes, int Tunneled, int Direct, string? Problem);

    public static VerifyResult VerifyApp(AppEntry app, string tunAddress, string lang = "ru")
    {
        var tunPrefix = TunPrefix(tunAddress);
        var pids = ProcessInspector.PidsOf(app);

        if (pids.Count == 0)
            return new VerifyResult(null, 0, 0, 0, Strings.T(lang, "verify_not_running"));

        int tunneled = 0, direct = 0, local = 0;
        foreach (var localAddr in ProcessInspector.LocalAddressesOf(pids))
        {
            if (localAddr.StartsWith(tunPrefix, StringComparison.Ordinal)) tunneled++;

            else if (IsLoopback(localAddr)) local++;
            else direct++;
        }

        if (tunneled + direct + local == 0)
            return new VerifyResult(null, pids.Count, 0, 0, Strings.T(lang, "verify_no_conn"));

        return new VerifyResult(direct == 0, pids.Count, tunneled, direct, null);
    }

    private static bool IsLoopback(string address) =>
        address.StartsWith("127.", StringComparison.Ordinal)
        || address is "::1" or "0.0.0.0" or "::";

    private static string TunPrefix(string tunAddress)
    {
        var ip = tunAddress.Split('/')[0];
        var lastDot = ip.LastIndexOf('.');
        return lastDot > 0 ? ip[..(lastDot + 1)] : ip;
    }

    private static string? Extract(string json, string key)
    {
        var i = json.IndexOf($"\"{key}\":\"", StringComparison.Ordinal);
        if (i < 0) return null;
        i += key.Length + 4;
        var j = json.IndexOf('"', i);
        return j < 0 ? null : json[i..j];
    }

    public static Task<bool> CheckSubscriptionLiveAsync(int mixedPort, string checkUrl) => Task.Run(() =>
    {
        var curl = Os.ResolveCurl();
        if (curl is null) return false;

        var devNull = Os.IsWindows ? "NUL" : "/dev/null";
        var (exit, output) = Os.Run(curl,
            $"-s -o {devNull} -w %{{http_code}} --max-time 15 -x socks5h://127.0.0.1:{mixedPort} " +
            "-H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36\" " + checkUrl, 20000);

        return exit == 0 && int.TryParse(output.Trim(), out var c) && c is >= 200 and < 400;
    });
}

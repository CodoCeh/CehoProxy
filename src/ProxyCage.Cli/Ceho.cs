using System.Net;
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
        Os.ResolveSingBox(Root) ?? Path.Combine(Root, Os.SingBoxFileName);

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

    private static HttpClient MakeClient(string? proxy = null, bool asBrowser = true, int timeoutSeconds = 15)
    {
        var handler = new HttpClientHandler();
        if (proxy is not null)
        {
            handler.Proxy = new WebProxyStub(proxy);
            handler.UseProxy = true;
        }
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
        if (asBrowser)
        {
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        }
        else
        {
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "CehoProxy/" + Updater.CurrentVersion);
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/plain,*/*");
        }
        return c;
    }

    private static bool LooksLikeWebPage(string body)
    {
        var head = body.TrimStart();
        return head.StartsWith('<')
            || head.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase);
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
        SubscriptionEntry sub, string lang, bool preferCache = false, Action<string>? onProgress = null, int timeoutSeconds = 15)
    {
        if (ReadWithoutNetwork(sub.Url, lang) is { Count: > 0 } local)
        {
            Mark(sub, true);
            return Tag(local, sub.Name);
        }

        var cache = SubCachePath(sub.Name);

        if (preferCache && File.Exists(cache))
        {
            var saved = SubscriptionParser.Parse(await File.ReadAllTextAsync(cache), lang);
            if (saved.Count > 0) return Tag(saved, sub.Name);
        }

        var (fresh, failure) = await FetchWithRetriesAsync(sub.Url, onProgress, lang, timeoutSeconds);
        if (fresh is not null)
        {
            onProgress?.Invoke(Strings.T(lang, "sub_parsing_nodes"));
            var freshNodes = SubscriptionParser.Parse(fresh, lang);
            if (freshNodes.Count > 0)
            {
                Directory.CreateDirectory(Root);
                await File.WriteAllTextAsync(cache, fresh);
                Mark(sub, true);
                return Tag(freshNodes, sub.Name);
            }
            if (!Quiet) Console.Error.WriteLine($"подписка «{sub.Name}» ответила, но нод в ответе нет");
        }
        else if (!Quiet) Console.Error.WriteLine($"подписка «{sub.Name}» не скачалась: {failure}");

        if (File.Exists(cache))
        {
            var cached = SubscriptionParser.Parse(await File.ReadAllTextAsync(cache), lang);
            if (cached.Count > 0)
            {
                Console.Error.WriteLine($"использую сохранённую копию подписки «{sub.Name}»");
                return Tag(cached, sub.Name);
            }
        }

        Mark(sub, false);
        return Array.Empty<ProxyNode>();
    }

    private const int FetchAttempts = 3;

    private static async Task<(string? Body, string? Failure)> FetchWithRetriesAsync(
        string url, Action<string>? onProgress = null, string lang = "ru", int timeoutSeconds = 15)
    {
        string? failure = null;
        string? webPage = null;
        for (var attempt = 1; attempt <= FetchAttempts; attempt++)
        {
            var asBrowser = attempt > 1;
            onProgress?.Invoke(Strings.T(lang, "sub_fetch_attempt", attempt, FetchAttempts));
            try
            {
                using var http = MakeClient(null, asBrowser, timeoutSeconds);
                using var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    onProgress?.Invoke(Strings.T(lang, "sub_reading_data"));
                    var body = await response.Content.ReadAsStringAsync();
                    if (body.Trim().Length == 0)
                    {
                        failure = $"пустой ответ (попытка {attempt})";
                        onProgress?.Invoke(failure);
                    }
                    else if (LooksLikeWebPage(body))
                    {
                        webPage ??= body;
                    }
                    else
                    {
                        var kb = Math.Max(1, body.Length / 1024);
                        onProgress?.Invoke(Strings.T(lang, "sub_parsing", kb));
                        return (body, null);
                    }
                }
                else
                {
                    failure = $"HTTP {(int)response.StatusCode}";
                    if (attempt < FetchAttempts)
                        onProgress?.Invoke(Strings.T(lang, "sub_fetch_retry", attempt, failure));
                }
            }
            catch (Exception ex)
            {
                var msg = ex is TaskCanceledException ? Strings.T(lang, "sub_timeout") : ex.Message;
                failure = msg;
                if (attempt < FetchAttempts)
                    onProgress?.Invoke(Strings.T(lang, "sub_fetch_retry", attempt, failure));
            }

            if (attempt < FetchAttempts) await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return webPage is not null ? (webPage, null) : (null, failure);
    }

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

    private static void Mark(SubscriptionEntry sub, bool ok)
    {
        try
        {
            var cfg = CehoConfig.Load(ConfigPath);
            var entry = cfg.Subscriptions.FirstOrDefault(s => s.Name == sub.Name);
            if (entry is null) return;
            entry.LastCheckOk = ok;
            entry.LastCheckedUtc = DateTime.UtcNow.ToString("u");
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
        CehoConfig cfg, bool preferCache = false, Action<string>? onProgress = null)
    {
        if (cfg.Subscriptions.Count == 0)
            throw new InvalidOperationException(Strings.T(cfg.Language, "pf_no_subs"));

        var lists = await Task.WhenAll(
            cfg.Subscriptions.Select(s => LoadOneAsync(s, cfg.Language, preferCache, onProgress, cfg.TimeoutSeconds)));

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

    public static async Task<string> ApplyAsync()
    {
        var cfg = CehoConfig.Load(ConfigPath);
        var nodes = await LoadAllNodesAsync(cfg);
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
            using var http = MakeClient($"http://127.0.0.1:{mixedPort}", false, timeoutSeconds);
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

    public static async Task<string> DiagnoseSubscriptionAsync(string url, string lang, int timeoutSeconds = 15)
    {
        var text = url.Trim();

        if (SubscriptionParser.LooksLikeNodeUri(text))
            return Strings.T(lang, "diag_node_uri_bad");

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return File.Exists(text)
                ? Strings.T(lang, "diag_file_bad")
                : Strings.T(lang, "diag_not_a_link");

        try
        {
            using var http = MakeClient(null, asBrowser: false, timeoutSeconds);
            HttpResponseMessage response = null!;
            for (var attempt = 1; attempt <= FetchAttempts; attempt++)
            {
                response?.Dispose();
                response = await http.GetAsync(uri);
                if (response.IsSuccessStatusCode) break;
                if (attempt < FetchAttempts) await Task.Delay(TimeSpan.FromSeconds(2));
            }
            using var _ = response;
            var code = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                return Strings.T(lang, code >= 500 ? "diag_server_down" : "diag_http_error", code);

            var body = await response.Content.ReadAsStringAsync();
            if (body.Trim().Length == 0) return Strings.T(lang, "diag_empty");

            var looksHtml = LooksLikeWebPage(body);
            return Strings.T(lang, looksHtml ? "diag_html" : "diag_unknown_format",
                body.Trim().Length);
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

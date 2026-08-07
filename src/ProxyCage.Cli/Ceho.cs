using System.Net;
using ProxyCage.Core;

namespace ProxyCage.Cli;

/// <summary>
/// Команды CehoProxy. И CLI, и веб-панель дёргают ОДИН этот слой —
/// иначе логика разъедется между двумя интерфейсами.
/// </summary>
public static class Ceho
{
    public static string Root =>
        Environment.GetEnvironmentVariable("CEHOPROXY_HOME") ?? Os.DefaultRoot;

    public static string ConfigPath => Path.Combine(Root, "config.json");
    public static string RuntimeConfigPath => Path.Combine(Root, "singbox.json");

    /// <summary>Движок ищем в папке настроек, рядом с собой и в PATH — см. <see cref="Os.ResolveSingBox"/>.</summary>
    public static string SingBoxPath =>
        Os.ResolveSingBox(Root) ?? Path.Combine(Root, Os.SingBoxFileName);

    private static string SubCachePath(string name) => Path.Combine(Root, $"sub-{name}.txt");

    /// <summary>
    /// Путь к программе для автозапуска и обёрток.
    ///
    /// Берём УСТАНОВЛЕННУЮ копию, а не ту, из которой запустились: установщик часто
    /// запускают из временной папки или из загрузок, и служба потом ссылалась бы на файл,
    /// который вот-вот удалят. Поймано живьём: юнит systemd указывал на /tmp.
    /// </summary>
    public static string OwnExecutablePath
    {
        get
        {
            var installed = Path.Combine(Root, Os.IsWindows ? "cehoproxy.exe" : "cehoproxy");
            if (File.Exists(installed)) return installed;
            return Environment.ProcessPath ?? (Os.IsWindows ? "cehoproxy.exe" : "/usr/local/bin/cehoproxy");
        }
    }

    /// <summary>Браузерные заголовки: без них Cloudflare отдаёт 403 независимо от блокировки.</summary>
    private static HttpClient MakeClient(string? proxy = null)
    {
        var handler = new HttpClientHandler();
        if (proxy is not null)
        {
            handler.Proxy = new WebProxyStub(proxy);
            handler.UseProxy = true;
        }
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        return c;
    }

    private sealed class WebProxyStub : IWebProxy
    {
        private readonly Uri _uri;
        public WebProxyStub(string uri) => _uri = new Uri(uri);
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) => _uri;
        public bool IsBypassed(Uri host) => host.IsLoopback;
    }

    /// <summary>Ноды одной подписки; при сбое сети берётся последняя удачная копия с диска.</summary>
    private static async Task<IReadOnlyList<ProxyNode>> LoadOneAsync(SubscriptionEntry sub, string lang)
    {
        // ссылка может и не быть ссылкой: одна нода целиком или файл на диске.
        // Тогда качать нечего — разбираем на месте
        if (ReadWithoutNetwork(sub.Url, lang) is { Count: > 0 } local)
        {
            Mark(sub, true);
            return Tag(local, sub.Name);
        }

        var cache = SubCachePath(sub.Name);

        // Кэш обновляем ТОЛЬКО годным ответом. Иначе страница-заглушка провайдера
        // (у неё бывает и код 200) затрёт последнюю рабочую копию, и продукт
        // останется вообще без нод — отказ там, где его можно было пережить.
        try
        {
            using var http = MakeClient();
            var fresh = await http.GetStringAsync(sub.Url);
            var freshNodes = SubscriptionParser.Parse(fresh, lang);
            if (freshNodes.Count > 0)
            {
                Directory.CreateDirectory(Root);
                await File.WriteAllTextAsync(cache, fresh);
                Mark(sub, true);
                return Tag(freshNodes, sub.Name);
            }
            Console.Error.WriteLine($"подписка «{sub.Name}» ответила, но нод в ответе нет");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"подписка «{sub.Name}» не скачалась: {ex.Message}");
        }

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

    /// <summary>Ноды из того, что уже есть под рукой: ссылка на ноду или файл. Иначе null.</summary>
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

    /// <summary>
    /// Отметка «работает» ставится по факту: отдала ЭТА подписка ноды или нет.
    /// Раньше состояние всех подписок выставлялось по общей пробе выхода, и мёртвая
    /// подписка, вернувшая пустоту, показывалась как рабочая — поймано живьём.
    /// </summary>
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

    /// <summary>
    /// ОБЩИЙ ПУЛ: ноды всех подписок разом, а не только «активной».
    ///
    /// Ротация по подпискам целиком слишком груба: в одной подписке часть нод живая, часть
    /// мёртвая, и переключать надо ноду, а не поставщика. Поэтому парсим всё, складываем
    /// в один пул, а выбор живой оставляем urltest внутри движка — он и есть ротация.
    /// Дедупликация по адресу и логину: один и тот же сервер часто встречается в двух подписках.
    /// </summary>
    public static async Task<IReadOnlyList<ProxyNode>> LoadAllNodesAsync(CehoConfig cfg)
    {
        if (cfg.Subscriptions.Count == 0)
            throw new InvalidOperationException(Strings.T(cfg.Language, "pf_no_subs"));

        var lists = await Task.WhenAll(cfg.Subscriptions.Select(s => LoadOneAsync(s, cfg.Language)));

        var pool = new List<ProxyNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var node in lists.SelectMany(l => l).Where(n => !n.IsMeta))
        {
            var key = $"{node.Protocol}|{node.Server}|{node.Port}|{node.Credential}";
            if (!seen.Add(key)) continue;
            node.Tag = $"n{++index:D3}";     // теги должны быть уникальны на весь конфиг
            pool.Add(node);
        }

        if (pool.Count == 0)
            throw new PoolEmptyException(Strings.T(cfg.Language, "pool_empty"));

        return pool;
    }

    /// <summary>Пересобирает боевой конфиг sing-box из текущего config.json.</summary>
    public static async Task<string> ApplyAsync()
    {
        var cfg = CehoConfig.Load(ConfigPath);
        var nodes = await LoadAllNodesAsync(cfg);
        var json = SingBoxConfigGenerator.GenerateForConfig(nodes, cfg);
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(RuntimeConfigPath, json);
        return Strings.T(cfg.Language, "rules_rebuilt");
    }

    /// <summary>Реальная проверка выхода — через локальный вход sing-box, а не «пинг».</summary>
    public static Task<(string? Country, string? Ip)> ProbeExitAsync(int mixedPort) => Task.Run(() =>
    {
        // Проба идёт через curl, а не через HttpClient. Причина не косметическая:
        // HttpClient с прокси на локальный mixed-инбаунд стабильно упирался в таймаут
        // (и в http-, и в socks5-режиме), тогда как curl тем же портом отвечал мгновенно.
        var curl = Os.ResolveCurl();
        if (curl is null) return (null, null);

        var (code, output) = Os.Run(curl,
            $"-s --max-time 15 -x socks5h://127.0.0.1:{mixedPort} http://ip-api.com/json", 20000);
        if (code != 0) return (null, null);

        return (Extract(output, "countryCode"), Extract(output, "query"));
    });

    /// <summary>
    /// Ротацию нод делает сам движок: пул собран из всех подписок, а urltest внутри него
    /// постоянно выбирает живую с наименьшей задержкой. Отдельно переключать ничего не надо.
    ///
    /// Эта проверка — про другой случай: не отвечает ВЕСЬ пул. Тогда переключаться не на что,
    /// и единственное осмысленное действие — перекачать подписки (провайдер мог заменить ноды)
    /// и пересобрать правила. Живость меряется настоящим запросом к адресу, который назначил
    /// пользователь, а не пингом: нода отвечает и будучи заблокированной.
    /// </summary>
    public static async Task<string?> RefreshIfDeadAsync(int mixedPort)
    {
        var cfg = CehoConfig.Load(ConfigPath);
        if (!cfg.RotationEnabled || cfg.Subscriptions.Count == 0) return null;

        if (await CheckSubscriptionLiveAsync(mixedPort, cfg.CheckUrl)) return null;

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

        // перезапускать туннель есть смысл, только если провайдер отдал другой список нод
        return SubscriptionsFingerprint() != before
            ? "подписки обновились, правила перечитаны"
            : "ни одна нода не отвечает";
    }

    /// <summary>Отпечаток кэшей подписок: сменился — значит провайдер отдал другой список нод.</summary>
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

    public sealed record VerifyResult(
        bool? Isolated, int Processes, int Tunneled, int Direct, string? Problem);

    /// <summary>
    /// Доказательство изоляции по ЖИВЫМ соединениям самого приложения.
    ///
    /// Почему не «подложить пробник в папку и сравнить IP»: в MSIX-папку
    /// (C:\Program Files\WindowsApps\...) писать нельзя — Windows запрещает, и для
    /// Store-приложений такая проверка невозможна в принципе. А главное, пробник
    /// доказывал бы изоляцию пробника, а не приложения.
    ///
    /// Здесь берём процессы приложения и смотрим локальный адрес их TCP-соединений:
    /// у завёрнутых в туннель он из подсети TUN, у утекающих мимо — реальный адрес
    /// сетевой карты. Это ровно тот признак, по которому изоляцию видно снаружи.
    /// </summary>
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
            // соединения внутри самой машины через туннель идти не могут и утечкой не являются:
            // без этой поправки продукт кричал бы «ТЕЧЁТ» там, где всё в порядке
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

    /// <summary>172.19.0.1/30 → «172.19.0.» — по этому префиксу и опознаём туннель.</summary>
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

    /// <summary>
    /// Живость подписки = настоящий запрос к адресу, который назначил пользователь.
    /// Браузерные заголовки обязательны: сайты за Cloudflare отдают 403 голому клиенту
    /// независимо от того, заблокирован выход или нет, и без них проверка врёт.
    /// </summary>
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

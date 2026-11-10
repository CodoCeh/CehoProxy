using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProxyCage.Core;

/// <summary>
/// Приём подписок с телефона по домашней сети: тот же протокол, что у экрана «Получить с телефона» в Android.
/// Порт открыт только на время ожидания, адрес содержит одноразовый секрет, принимается одна отправка.
/// </summary>
public sealed class LanReceiver : IDisposable
{
    public const string ServiceType = "_cehoproxy._tcp";
    public const int MaxBody = 1_000_000;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly string _token = NewToken();
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private DnsSdAnnouncer? _announcer;
    private string? _firewallRule;
    private string? _payload;

    public string Name { get; }
    public string Host { get; private set; } = "";
    public int Port { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public bool Active { get; private set; }
    public string Url => $"http://{Host}:{Port}/p/{_token}";

    /// <summary>Принятый текст (ссылка подписки или JSON приложения); null, пока ничего не пришло.</summary>
    public string? Payload { get { lock (_gate) return _payload; } }

    public LanReceiver(string name) => Name = string.IsNullOrWhiteSpace(name) ? "CehoProxy" : name.Trim();

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Адрес компьютера в домашней сети: частный IPv4 рабочего Wi-Fi или кабеля, не туннель и не виртуалка.</summary>
    public static IPAddress? FindLanAddress()
    {
        IPAddress? best = null;
        var bestScore = -1;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.Ethernet3Megabit)) continue;
            var name = (nic.Name + " " + nic.Description).ToLowerInvariant();
            if (name.Contains("tun") || name.Contains("wintun") || name.Contains("vpn") || name.Contains("tailscale")
                || name.Contains("vmware") || name.Contains("virtualbox") || name.Contains("hyper-v") || name.Contains("vethernet")
                || name.Contains("docker") || name.Contains("loopback") || name.Contains("zerotier")) continue;
            var props = nic.GetIPProperties();
            foreach (var u in props.UnicastAddresses)
            {
                var ip = u.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || !IsPrivate(ip)) continue;
                var score = (props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                 && !g.Address.Equals(IPAddress.Any)) ? 2 : 0)
                            + (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0);
                if (score > bestScore) { best = ip; bestScore = score; }
            }
        }
        return best;
    }

    public static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
    }

    /// <summary>Открывает приём. Возвращает false, если нет домашней сети.</summary>
    public bool Start(Action<string>? log = null)
    {
        lock (_gate)
        {
            if (Active) return true;
            var ip = FindLanAddress();
            if (ip is null) return false;
            var listener = new TcpListener(ip, 0);
            listener.Start(4);
            _listener = listener;
            Host = ip.ToString();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            ExpiresUtc = DateTime.UtcNow + Lifetime;
            _stop = new CancellationTokenSource();
            Active = true;
            _firewallRule = LanFirewall.Allow(Port, log);
            _announcer = DnsSdAnnouncer.TryStart(Name, ServiceType, ip, Port, _token, log);
            var token = _stop.Token;
            _ = Task.Run(() => AcceptLoopAsync(listener, token));
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(Lifetime, token); Stop(); } catch (OperationCanceledException) { }
            });
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!Active) return;
            Active = false;
            try { _stop?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            _announcer?.Dispose();
            _announcer = null;
            LanFirewall.Remove(_firewallRule);
            _firewallRule = null;
        }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(async () =>
            {
                try { using (client) await HandleAsync(client, ct); } catch { }
            });
        }
    }

    internal static bool TokenMatches(string path, string token)
    {
        var p = path.Split('?')[0];
        const string prefix = "/p/";
        if (!p.StartsWith(prefix, StringComparison.Ordinal) || p.Length != prefix.Length + 32) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(p[prefix.Length..]), Encoding.ASCII.GetBytes(token));
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var stream = client.GetStream();
        var head = new MemoryStream();
        var buffer = new byte[4096];
        var split = -1;
        while (split < 0 && head.Length < 16 * 1024)
        {
            var n = await stream.ReadAsync(buffer, timeout.Token);
            if (n <= 0) return;
            head.Write(buffer, 0, n);
            split = IndexOf(head.GetBuffer(), (int)head.Length, "\r\n\r\n"u8);
        }
        if (split < 0) return;

        var all = head.ToArray();
        var lines = Encoding.ASCII.GetString(all, 0, split).Split("\r\n");
        var request = lines[0].Split(' ');
        if (request.Length < 2) return;
        var method = request[0];
        var path = request[1];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var i = line.IndexOf(':');
            if (i > 0) headers[line[..i].Trim()] = line[(i + 1)..].Trim();
        }

        if (!TokenMatches(path, _token))
        {
            await RespondAsync(stream, 404, Page("Ссылка устарела", "<p>На компьютере снова откройте «С другого устройства».</p>"), timeout.Token);
            return;
        }
        if (method == "GET")
        {
            await RespondAsync(stream, 200, FormPage(null), timeout.Token);
            return;
        }
        if (method != "POST")
        {
            await RespondAsync(stream, 405, Page("Ошибка", "<p>Запрос не понят.</p>"), timeout.Token);
            return;
        }
        if (!int.TryParse(headers.GetValueOrDefault("Content-Length"), out var length))
        {
            await RespondAsync(stream, 411, Page("Ошибка", "<p>Запрос не понят.</p>"), timeout.Token);
            return;
        }
        if (length is < 1 or > MaxBody)
        {
            await RespondAsync(stream, 413, Page("Слишком много данных", "<p>Отправьте только ссылку на подписку.</p>"), timeout.Token);
            return;
        }

        var body = new byte[length];
        var have = Math.Min(all.Length - (split + 4), length);
        Array.Copy(all, split + 4, body, 0, have);
        while (have < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(have, length - have), timeout.Token);
            if (n <= 0) break;
            have += n;
        }
        var text = Encoding.UTF8.GetString(body, 0, have);
        var payload = headers.GetValueOrDefault("Content-Type", "").StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            ? FormField(text, "link")
            : text.Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            await RespondAsync(stream, 400, FormPage("Вставьте ссылку на подписку."), timeout.Token);
            return;
        }

        lock (_gate)
        {
            if (!Active || _payload is not null) return;
            _payload = payload;
        }
        await RespondAsync(stream, 200, Page("Отправлено", "<p>Подтвердите добавление на экране компьютера.</p>"), timeout.Token);
        Stop();
    }

    internal static string? FormField(string body, string name)
    {
        foreach (var pair in body.Split('&'))
        {
            var i = pair.IndexOf('=');
            if (i < 0 || pair[..i] != name) continue;
            var value = Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' ')).Trim();
            return value.Length > 0 ? value : null;
        }
        return null;
    }

    internal string AppLink => "cehoproxy://pair?u=" + Uri.EscapeDataString(Url);

    private string FormPage(string? error) => Page("Отправить подписку на компьютер",
        "<p>Приложение CehoProxy уже на телефоне? Откройте его кнопкой, оно само отправит подписки.</p>"
        + "<a class=\"app\" href=\"" + WebUtility.HtmlEncode(AppLink) + "\">Открыть в CehoProxy</a>"
        + "<p>Приложения нет, например на iPhone? Вставьте ссылку на подписку здесь:</p>"
        + (error is null ? "" : "<p class=\"error\">" + WebUtility.HtmlEncode(error) + "</p>")
        + "<form method=\"post\" action=\"/p/" + _token + "\"><textarea name=\"link\" rows=\"4\" placeholder=\"https://…\"></textarea>"
        + "<button type=\"submit\">Отправить на компьютер</button></form>");

    private static string Page(string title, string body) =>
        "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
        + "<meta name=\"color-scheme\" content=\"dark\"><title>" + WebUtility.HtmlEncode(title) + "</title>"
        + "<style>body{font-family:system-ui,sans-serif;margin:0;padding:24px;background:#1c1b17;color:#e9e6dc;line-height:1.5}"
        + "h1{font-size:24px;margin:0 0 16px}p{color:#c7c4b8}button{display:block;width:100%;box-sizing:border-box;margin:12px 0 24px;padding:14px;border:0;"
        + "border-radius:14px;background:#a9c29a;color:#1f2a1b;font-size:16px;font-weight:600}"
        + "textarea{width:100%;box-sizing:border-box;padding:12px;border-radius:12px;border:1px solid #4a4a40;background:#26251f;color:#e9e6dc;font-size:15px}"
        + "a.app{display:block;box-sizing:border-box;margin:12px 0 24px;padding:14px;border-radius:14px;background:#a9c29a;color:#1f2a1b;font-size:16px;font-weight:600;text-align:center;text-decoration:none}"
        + ".error{color:#f2b8b5}</style></head><body><h1>" + WebUtility.HtmlEncode(title) + "</h1>" + body + "</body></html>";

    private static async Task RespondAsync(NetworkStream stream, int status, string html, CancellationToken ct)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 404 => "Not Found", 405 => "Method Not Allowed", 411 => "Length Required", _ => "Payload Too Large" };
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    private static int IndexOf(byte[] data, int length, ReadOnlySpan<byte> marker)
        => data.AsSpan(0, length).IndexOf(marker);

    /// <summary>Что прислал телефон: ссылки подписок или ссылки отдельных серверов.</summary>
    public static List<string> ExtractLinks(string payload)
    {
        var result = new List<string>();
        var text = payload.Trim();
        if (text.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var profile = root.TryGetProperty("profile", out var p) && p.ValueKind == JsonValueKind.Object ? p : root;
                if (profile.TryGetProperty("subscriptionUrls", out var urls) && urls.ValueKind == JsonValueKind.Array)
                    foreach (var u in urls.EnumerateArray())
                        if (u.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(u.GetString())) result.Add(u.GetString()!.Trim());
                if (result.Count == 0 && profile.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                    foreach (var n in nodes.EnumerateArray())
                        if (n.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(raw.GetString()))
                            result.Add(raw.GetString()!.Trim());
            }
            catch (JsonException) { }
        }
        else
        {
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (line.Contains("://", StringComparison.Ordinal)) result.Add(line);
        }
        return result.Distinct().Take(100).ToList();
    }
}

/// <summary>Временное правило брандмауэра Windows: только домашняя сеть и только на время приёма.</summary>
internal static class LanFirewall
{
    private const string RuleName = "CehoProxy receive";

    public static string? Allow(int port, Action<string>? log)
    {
        if (!Os.IsWindows) return null;
        Remove(RuleName);
        var (code, output) = Os.Run("netsh", $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port} remoteip=localsubnet profile=any", 10000);
        if (code != 0) { log?.Invoke($"приём с телефона: правило брандмауэра не добавлено ({code}): {output.Trim()}"); return null; }
        return RuleName;
    }

    public static void Remove(string? name)
    {
        if (!Os.IsWindows || name is null) return;
        Os.Run("netsh", $"advfirewall firewall delete rule name=\"{name}\"", 10000);
    }
}

/// <summary>Объявляет в домашней сети службу _cehoproxy._tcp (mDNS), чтобы телефон нашёл компьютер в списке.</summary>
internal sealed class DnsSdAnnouncer : IDisposable
{
    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    private readonly UdpClient _listen;
    private readonly UdpClient _send;
    private readonly CancellationTokenSource _stop = new();
    private readonly byte[] _answer;
    private readonly byte[] _goodbye;
    private readonly string _serviceName;
    private readonly string _instanceName;
    private readonly string _hostName;

    private DnsSdAnnouncer(UdpClient listen, UdpClient send, string name, string type, IPAddress ip, int port, string token)
    {
        _listen = listen;
        _send = send;
        _serviceName = type + ".local";
        _instanceName = name + "." + _serviceName;
        _hostName = Sanitize(Environment.MachineName) + "-ceho.local";
        _answer = BuildAnswer(ip, port, token, 120);
        _goodbye = BuildAnswer(ip, port, token, 0);
    }

    public static DnsSdAnnouncer? TryStart(string name, string type, IPAddress ip, int port, string token, Action<string>? log)
    {
        UdpClient? listen = null, send = null;
        try
        {
            listen = new UdpClient(AddressFamily.InterNetwork);
            listen.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            if (!Os.IsWindows)
            {
                try { listen.Client.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)(OperatingSystem.IsMacOS() ? 0x200 : 15) /* SO_REUSEPORT */, true); } catch { }
            }
            listen.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
            listen.JoinMulticastGroup(Group, ip);

            send = new UdpClient(new IPEndPoint(ip, 0));
            send.MulticastLoopback = true;
            send.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ip.GetAddressBytes());
            send.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);

            var self = new DnsSdAnnouncer(listen, send, name, type, ip, port, token);
            _ = Task.Run(self.LoopAsync);
            _ = Task.Run(async () =>
            {
                for (var i = 0; i < 3 && !self._stop.IsCancellationRequested; i++)
                {
                    self.Announce();
                    try { await Task.Delay(1000, self._stop.Token); } catch (OperationCanceledException) { }
                }
            });
            return self;
        }
        catch (Exception ex)
        {
            log?.Invoke("приём с телефона: объявление в сети не запущено: " + ex.Message);
            listen?.Dispose();
            send?.Dispose();
            return null;
        }
    }

    private void Announce()
    {
        try { _send.Send(_answer, _answer.Length, new IPEndPoint(Group, MdnsPort)); } catch { }
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await _listen.ReceiveAsync(_stop.Token); }
            catch { break; }
            try { if (IsAboutUs(packet.Buffer)) Announce(); } catch { }
        }
    }

    private bool IsAboutUs(byte[] data)
    {
        if (data.Length < 12 || (data[2] & 0x80) != 0) return false;
        var count = (data[4] << 8) | data[5];
        var pos = 12;
        for (var i = 0; i < count; i++)
        {
            var name = ReadName(data, ref pos);
            if (name is null || pos + 4 > data.Length) return false;
            pos += 4;
            if (name.Equals(_serviceName, StringComparison.OrdinalIgnoreCase)
                || name.Equals(_instanceName, StringComparison.OrdinalIgnoreCase)
                || name.Equals(_hostName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? ReadName(byte[] data, ref int pos)
    {
        var labels = new List<string>();
        var p = pos;
        var jumped = false;
        for (var guard = 0; guard < 64; guard++)
        {
            if (p >= data.Length) return null;
            var len = data[p];
            if (len == 0) { if (!jumped) pos = p + 1; return string.Join('.', labels); }
            if ((len & 0xC0) == 0xC0)
            {
                if (p + 1 >= data.Length) return null;
                var target = ((len & 0x3F) << 8) | data[p + 1];
                if (!jumped) pos = p + 2;
                jumped = true;
                p = target;
                continue;
            }
            if (p + 1 + len > data.Length) return null;
            labels.Add(Encoding.UTF8.GetString(data, p + 1, len));
            p += 1 + len;
        }
        return null;
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(c => char.IsLetterOrDigit(c) && c < 128 || c == '-').ToArray();
        return chars.Length == 0 ? "pc" : new string(chars);
    }

    private static void WriteName(MemoryStream ms, string name)
    {
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length > 63) bytes = bytes[..63];
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0);
    }

    private static void U16(MemoryStream ms, int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
    private static void U32(MemoryStream ms, int v) { U16(ms, v >> 16); U16(ms, v & 0xFFFF); }

    private static void Record(MemoryStream ms, string name, int type, int cls, int ttl, byte[] data)
    {
        WriteName(ms, name);
        U16(ms, type);
        U16(ms, cls);
        U32(ms, ttl);
        U16(ms, data.Length);
        ms.Write(data);
    }

    private byte[] BuildAnswer(IPAddress ip, int port, string token, int ttl)
    {
        var ms = new MemoryStream();
        U16(ms, 0); U16(ms, 0x8400); U16(ms, 0); U16(ms, 4); U16(ms, 0); U16(ms, 0);

        var ptr = new MemoryStream(); WriteName(ptr, _instanceName);
        Record(ms, _serviceName, 12, 0x0001, ttl, ptr.ToArray());

        var srv = new MemoryStream(); U16(srv, 0); U16(srv, 0); U16(srv, port); WriteName(srv, _hostName);
        Record(ms, _instanceName, 33, 0x8001, ttl, srv.ToArray());

        var txt = new MemoryStream();
        foreach (var entry in new[] { "t=" + token, "h=" + ip })
        {
            var b = Encoding.UTF8.GetBytes(entry);
            txt.WriteByte((byte)b.Length);
            txt.Write(b);
        }
        Record(ms, _instanceName, 16, 0x8001, ttl, txt.ToArray());
        Record(ms, _hostName, 1, 0x8001, ttl, ip.GetAddressBytes());
        return ms.ToArray();
    }

    public void Dispose()
    {
        try { _send.Send(_goodbye, _goodbye.Length, new IPEndPoint(Group, MdnsPort)); } catch { }
        try { _stop.Cancel(); } catch { }
        try { _listen.Dispose(); } catch { }
        try { _send.Dispose(); } catch { }
    }
}

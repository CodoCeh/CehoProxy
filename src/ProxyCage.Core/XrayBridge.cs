using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxyCage.Core;

public static class XrayBridge
{
    public const string Version = "26.3.27";
    private const int FirstPort = 20808;

    private static readonly Dictionary<string, string> Digests = new()
    {
        ["windows-64"] = "d004c39288ce9ada487c6f398c7c545f7d749e44bdfdd59dbc9f865afba4e1ad",
        ["windows-arm64-v8a"] = "35d4ed6ec21224fb22b07c2c3f672e2350cd536f2c74d309150175a76365ea88",
        ["linux-64"] = "23cd9af937744d97776ee35ecad4972cf4b2109d1e0fe6be9930467608f7c8ae",
        ["linux-arm64-v8a"] = "4d30283ae614e3057f730f67cd088a42be6fdf91f8639d82cb69e48cde80413c",
        ["macos-64"] = "f5b0471d3459eff1b82e48af0aeac186abcc3298210070afbbbd8437a4e8b203",
        ["macos-arm64-v8a"] = "2e93a67e8aa1936ecefb307e120830fcbd4c643ab9b1c46a2d0838d5f8409eaf",
    };

    public static string FileName => Os.IsWindows ? "ceho-xray.exe" : "ceho-xray";
    private static string ProcessName => "ceho-xray";

    public static string BinaryPath(string root) => Path.Combine(root, FileName);
    public static string ConfigPath(string root) => Path.Combine(root, "xray.json");
    public static string LogPath(string root) => Path.Combine(root, "xray.log");

    public static bool Supports(ProxyNode node) =>
        node.Protocol == ProxyProtocol.Vless && node.Network is "xhttp" or "splithttp";

    public static async Task<IReadOnlyList<ProxyNode>> PrepareAsync(
        IReadOnlyList<ProxyNode> nodes, string root, Action<string> log, string lang = "ru")
    {
        foreach (var node in nodes) node.BridgePort = null;
        var bridged = nodes.Where(Supports).ToList();
        if (bridged.Count == 0)
        {
            TryDelete(ConfigPath(root));
            return nodes;
        }

        if (!await EnsureBinaryAsync(root, log, lang))
        {
            TryDelete(ConfigPath(root));
            return nodes.Where(n => !Supports(n)).ToList();
        }

        var ours = OwnPorts(root);
        var port = FirstPort;
        foreach (var node in bridged)
        {
            while (!ours.Contains(port) && !PortFree(port)) port++;
            node.BridgePort = port++;
        }
        Directory.CreateDirectory(root);
        PrivateFile.Write(ConfigPath(root), Config(bridged, LogPath(root)));
        return nodes;
    }

    public static string Config(IEnumerable<ProxyNode> nodes, string? logFile = null)
    {
        var inbounds = new JsonArray();
        var outbounds = new JsonArray();
        var rules = new JsonArray();
        foreach (var node in nodes)
        {
            var port = node.BridgePort ?? throw new InvalidOperationException($"нет локального порта для {node.Tag}");
            inbounds.Add(new JsonObject
            {
                ["tag"] = "in-" + node.Tag,
                ["listen"] = "127.0.0.1",
                ["port"] = port,
                ["protocol"] = "socks",
                ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = true },
            });
            outbounds.Add(Outbound(node));
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray("in-" + node.Tag),
                ["outboundTag"] = "out-" + node.Tag,
            });
        }
        return new JsonObject
        {
            ["log"] = logFile is null
                ? new JsonObject { ["loglevel"] = "warning" }
                : new JsonObject { ["loglevel"] = "warning", ["error"] = logFile, ["access"] = "none" },
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["routing"] = new JsonObject { ["rules"] = rules },
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Outbound(ProxyNode n)
    {
        var user = new JsonObject { ["id"] = n.Credential, ["encryption"] = "none" };
        if (!string.IsNullOrEmpty(n.Flow)) user["flow"] = n.Flow;

        var stream = new JsonObject { ["network"] = "xhttp" };
        switch (n.Security)
        {
            case "reality":
                var reality = new JsonObject
                {
                    ["serverName"] = n.Sni ?? "",
                    ["publicKey"] = n.PublicKey ?? "",
                    ["fingerprint"] = string.IsNullOrEmpty(n.Fingerprint) ? "chrome" : n.Fingerprint,
                };
                if (!string.IsNullOrEmpty(n.ShortId)) reality["shortId"] = n.ShortId;
                if (!string.IsNullOrEmpty(n.SpiderX)) reality["spiderX"] = n.SpiderX;
                stream["security"] = "reality";
                stream["realitySettings"] = reality;
                break;
            case "tls":
                var tls = new JsonObject
                {
                    ["serverName"] = n.Sni ?? n.Server,
                    ["fingerprint"] = string.IsNullOrEmpty(n.Fingerprint) ? "chrome" : n.Fingerprint,
                };
                if (!string.IsNullOrEmpty(n.PinnedCertSha256)) tls["pinnedPeerCertSha256"] = n.PinnedCertSha256;
                if (!string.IsNullOrEmpty(n.Alpn))
                    tls["alpn"] = new JsonArray(n.Alpn.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => (JsonNode)a.Trim()).ToArray());
                stream["security"] = "tls";
                stream["tlsSettings"] = tls;
                break;
            default:
                stream["security"] = "none";
                break;
        }

        var xhttp = new JsonObject
        {
            ["mode"] = string.IsNullOrEmpty(n.XhttpMode) ? "auto" : n.XhttpMode,
            ["path"] = string.IsNullOrEmpty(n.Path) ? "/" : n.Path,
        };
        if (!string.IsNullOrEmpty(n.Host)) xhttp["host"] = n.Host;
        if (!string.IsNullOrEmpty(n.XhttpExtra))
        {
            try { if (JsonNode.Parse(n.XhttpExtra) is JsonObject extra) xhttp["extra"] = extra; }
            catch (JsonException) { }
        }
        stream["xhttpSettings"] = xhttp;

        return new JsonObject
        {
            ["tag"] = "out-" + n.Tag,
            ["protocol"] = "vless",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray(new JsonObject
                {
                    ["address"] = n.Server,
                    ["port"] = n.Port,
                    ["users"] = new JsonArray(user),
                }),
            },
            ["streamSettings"] = stream,
        };
    }

    public static void Start(string root, Action<string>? log = null)
    {
        Stop();
        var config = ConfigPath(root);
        var binary = BinaryPath(root);
        if (!File.Exists(config) || !File.Exists(binary)) return;

        TryDelete(LogPath(root));
        var start = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(config);
        using var process = Process.Start(start);
        if (process is null) return;
        log?.Invoke($"мост XHTTP запущен, pid {process.Id}");
    }

    public static bool IsRunning() => Process.GetProcessesByName(ProcessName).Length > 0;

    public static void Stop()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try { process.Kill(); process.WaitForExit(3000); }
            catch { }
            finally { process.Dispose(); }
        }
    }

    public static async Task<bool> EnsureBinaryAsync(string root, Action<string> log, string lang = "ru")
    {
        var binary = BinaryPath(root);
        if (File.Exists(binary)) return true;

        var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var platform = Os.Kind switch
        {
            OsKind.Windows => arm ? "windows-arm64-v8a" : "windows-64",
            OsKind.Mac => arm ? "macos-arm64-v8a" : "macos-64",
            _ => arm ? "linux-arm64-v8a" : "linux-64",
        };
        var archive = Path.Combine(root, $"Xray-{platform}.zip");
        try
        {
            log(Strings.T(lang, "xray_downloading", Version));
            using (var http = DirectHttp.CreateClient(TimeSpan.FromMinutes(10)))
            await using (var stream = await http.GetStreamAsync($"https://github.com/XTLS/Xray-core/releases/download/v{Version}/Xray-{platform}.zip"))
            await using (var file = File.Create(archive))
                await stream.CopyToAsync(file);

            string actual;
            await using (var file = File.OpenRead(archive))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
            if (actual != Digests[platform])
                throw new InvalidOperationException($"контрольная сумма Xray не совпала: {actual}");

            using (var zip = ZipFile.OpenRead(archive))
            {
                var entry = zip.GetEntry(Os.IsWindows ? "xray.exe" : "xray")
                            ?? throw new InvalidOperationException("в архиве Xray нет программы");
                entry.ExtractToFile(binary + ".tmp", overwrite: true);
            }
            if (!Os.IsWindows) File.SetUnixFileMode(binary + ".tmp", (UnixFileMode)0b111_101_101);
            File.Move(binary + ".tmp", binary, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException)
        {
            log(Strings.T(lang, "xray_download_failed", ex.Message));
            TryDelete(binary + ".tmp");
            return false;
        }
        finally { TryDelete(archive); }
    }

    private static HashSet<int> OwnPorts(string root)
    {
        var ports = new HashSet<int>();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(ConfigPath(root)))?["inbounds"] is JsonArray inbounds)
                foreach (var inbound in inbounds)
                    if (inbound?["port"]?.GetValue<int>() is { } p) ports.Add(p);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException) { }
        return ports;
    }

    private static bool PortFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

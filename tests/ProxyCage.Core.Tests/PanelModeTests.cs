using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class PanelModeTests : IDisposable
{
    private static readonly string[] Jargon =
        { "нод", "пул", "vless", "vmess", "trojan", "hysteria", "tuic", "naive", "shadowsocks", "sing-box" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-mode-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http;
    private string ConfigPath => Path.Combine(_root, "config.json");

    public PanelModeTests()
    {
        Directory.CreateDirectory(_root);
        TestEngine.Place(_root);
        new CehoConfig
        {
            PanelMode = CehoConfig.PanelModeSimple,
            Apps = { new AppEntry { Name = "app", Folder = Os.IsWindows ? @"C:\Games\App" : "/tmp" } },
            Subscriptions =
            {
                new SubscriptionEntry { Name = "sub", Url = "https://example.invalid/sub", LastNodes = 3 },
                new SubscriptionEntry { Name = "own", Url = "naive+https://u:p@example.invalid:443" },
            },
        }.Save(ConfigPath);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        _web = new WebServer(ConfigPath, () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        _web.Start(port);
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
    }

    public void Dispose()
    {
        _web.Stop();
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static string Visible(string html) =>
        WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<(script|style)[\\s\\S]*?</\\1>", " "), "<[^>]+>", " "));

    [Theory]
    [InlineData("state")]
    [InlineData("apps")]
    [InlineData("subs")]
    [InlineData("help")]
    public async Task Simple_mode_speaks_without_jargon(string tab)
    {
        var text = Visible(await _http.GetStringAsync($"/?tab={tab}")).ToLowerInvariant();
        foreach (var word in Jargon)
            Assert.DoesNotMatch($"(?<![\\w/@.-]){Regex.Escape(word)}", text);
    }

    [Fact]
    public async Task Simple_mode_shows_four_tabs_and_pro_shows_all()
    {
        var simple = await _http.GetStringAsync("/");
        Assert.DoesNotContain("/?tab=exit", simple);
        Assert.DoesNotContain("/?tab=log", simple);
        Assert.Contains("/?tab=help", simple);

        await Switch(CehoConfig.PanelModePro);
        var pro = await _http.GetStringAsync("/");
        foreach (var tab in new[] { "sites", "exit", "doctor", "access", "browser", "log", "help" })
            Assert.Contains($"/?tab={tab}", pro);
    }

    [Fact]
    public async Task Switching_mode_changes_nothing_but_the_mode()
    {
        var before = File.ReadAllText(ConfigPath);
        var written = File.GetLastWriteTimeUtc(ConfigPath);

        await Switch(CehoConfig.PanelModePro);
        await Switch(CehoConfig.PanelModeSimple);

        Assert.Equal(before, File.ReadAllText(ConfigPath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(ConfigPath));
    }

    [Fact]
    public void New_install_starts_simple_and_existing_config_stays_pro()
    {
        Assert.True(CehoConfig.Load(Path.Combine(_root, "missing.json")).SimplePanel);

        var old = Path.Combine(_root, "old.json");
        File.WriteAllText(old, "{\"Apps\":[]}");
        Assert.False(CehoConfig.Load(old).SimplePanel);
    }

    private async Task Switch(string mode)
    {
        using var reply = await _http.PostAsync("/mode", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["mode"] = mode,
            ["tab"] = "state",
        }));
        Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
    }
}

public class ExitSaveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-exit-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http;
    private string ConfigPath => Path.Combine(_root, "config.json");

    public ExitSaveTests()
    {
        Directory.CreateDirectory(_root);
        new CehoConfig { ExcludedCountries = { "RU" }, BlockedNodes = { "Vless|old|443" } }.Save(ConfigPath);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        _web = new WebServer(ConfigPath, () => new WebServer.ControlState(false, null, null, null, false), _ => { })
        {
            OnApply = _ => Task.FromResult("ok"),
        };
        _web.Start(port);
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
    }

    public void Dispose()
    {
        _web.Stop();
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task One_save_writes_countries_nodes_and_check_settings()
    {
        using var reply = await _http.PostAsync("/exit/save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tab"] = "exit",
            ["call"] = "RU,NL,DE",
            ["c_RU"] = "on",
            ["c_NL"] = "on",
            ["nall"] = "Vless|a|443\nVless|b|443",
            ["n_Vless|a|443"] = "on",
            ["checkurl"] = "https://example.org/204",
            ["speed"] = "700",
            ["timeout"] = "30",
        }));
        Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);

        var cfg = CehoConfig.Load(ConfigPath);
        Assert.Equal(new[] { "DE" }, cfg.ExcludedCountries);
        Assert.Contains("Vless|b|443", cfg.BlockedNodes);
        Assert.Contains("Vless|old|443", cfg.BlockedNodes);
        Assert.DoesNotContain("Vless|a|443", cfg.BlockedNodes);
        Assert.Equal("https://example.org/204", cfg.CheckUrl);
        Assert.Equal(700, cfg.MaxLatencyMs);
        Assert.Equal(30, cfg.TimeoutSeconds);
        Assert.False(cfg.RotationEnabled);
    }

    [Fact]
    public async Task Countries_missing_from_the_pool_stay_excluded()
    {
        using var reply = await _http.PostAsync("/exit/save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tab"] = "exit",
            ["call"] = "UZ",
            ["c_UZ"] = "on",
            ["timeout"] = "30",
        }));
        Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
        Assert.Equal(new[] { "RU" }, CehoConfig.Load(ConfigPath).ExcludedCountries);
    }
}

public class ShortPathTests
{
    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\Antigravity", @"…\Programs\Antigravity")]
    [InlineData("/Applications/Cursor.app", "/Applications/Cursor.app")]
    [InlineData("/home/me/.local/share/app", "…/share/app")]
    [InlineData(@"C:\Tools\x", @"C:\Tools\x")]
    public void Long_paths_keep_the_last_two_folders(string path, string expected) =>
        Assert.Equal(expected, WebServer.ShortPath(path));
}

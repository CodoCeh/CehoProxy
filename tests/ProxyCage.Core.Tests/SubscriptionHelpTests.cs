using System.Net;
using System.Net.Sockets;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class SubscriptionHelpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-subhelp-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http;
    private string ConfigPath => Path.Combine(_root, "config.json");

    public SubscriptionHelpTests()
    {
        Directory.CreateDirectory(_root);
        new CehoConfig { PanelMode = CehoConfig.PanelModeSimple }.Save(ConfigPath);

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

    [Theory]
    [InlineData(CehoConfig.PanelModeSimple)]
    [InlineData(CehoConfig.PanelModePro)]
    public async Task Help_says_where_a_subscription_comes_from(string mode)
    {
        await Switch(mode);
        var help = await _http.GetStringAsync("/?tab=help");

        Assert.Contains(Strings.T("ru", "sub_where_title"), help);
        Assert.Contains(Strings.T("ru", "sub_where_formats", mode == CehoConfig.PanelModeSimple), help);
        Assert.Contains("https://t.me/CeBers_VPN_bot", help);
    }

    [Fact]
    public async Task Help_lists_only_the_formats_the_parser_knows()
    {
        await Switch(CehoConfig.PanelModePro);
        var help = await _http.GetStringAsync("/?tab=help");

        foreach (var scheme in new[] { "vless://", "vmess://", "trojan://", "ss://", "hysteria2://", "tuic://", "naive://" })
        {
            Assert.Contains(scheme, help);
            Assert.True(SubscriptionParser.LooksLikeNodeUri(scheme + "x"));
        }
    }

    [Fact]
    public async Task The_first_wizard_step_points_at_that_help()
    {
        var page = await _http.GetStringAsync("/");

        Assert.Contains(Strings.T("ru", "wiz_no_link"), page);
        Assert.Contains("href=\"/?tab=help\"", page);
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

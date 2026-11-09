using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class LightPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-light-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http = new();
    private readonly HttpClient _skin = new();
    private readonly int _panelPort;

    public LightPanelTests()
    {
        Directory.CreateDirectory(_root);
        new CehoConfig { PanelMode = CehoConfig.PanelModeSimple }.Save(Path.Combine(_root, "config.json"));
        _web = new WebServer(Path.Combine(_root, "config.json"), () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        _panelPort = TestPanel.Start(_web);
        _skin.DefaultRequestHeaders.Add("Cookie", "ceho-skin=light");
    }

    public void Dispose()
    {
        _web.Stop();
        _http.Dispose();
        _skin.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Light_is_announced_on_the_panel_port()
    {
        Assert.Equal(_panelPort, Auth.ReadPanelPointer(_root));
        Assert.Equal(_panelPort, Auth.ReadLightPointer(_root));
    }

    [Fact]
    public async Task The_light_cookie_gives_the_android_skin_and_plain_requests_do_not()
    {
        var light = await _skin.GetStringAsync($"http://127.0.0.1:{_panelPort}/");
        var panel = await _http.GetStringAsync($"http://127.0.0.1:{_panelPort}/");

        Assert.Contains("<html lang=ru class=light>", light);
        Assert.Contains("CehoProxy Light", light);
        Assert.Contains("id=light-speed", light);
        Assert.Contains("class=\"light-vortex", light);
        Assert.DoesNotContain("class=light", panel);
        Assert.DoesNotContain("light-speed", panel);
    }

    [Fact]
    public async Task The_light_path_sets_the_skin_cookie_and_returns_to_the_panel()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler);
        var reply = await http.GetAsync($"http://127.0.0.1:{_panelPort}/light");

        Assert.Equal(System.Net.HttpStatusCode.Found, reply.StatusCode);
        Assert.Contains("ceho-skin=light", string.Join(";", reply.Headers.GetValues("Set-Cookie")));
        Assert.Equal("/", reply.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Both_skins_show_the_same_settings()
    {
        var cfg = CehoConfig.Load(Path.Combine(_root, "config.json"));
        cfg.Apps.Add(new AppEntry { Name = "Zed", Folder = "/Applications/Zed.app", Enabled = true });
        cfg.Save(Path.Combine(_root, "config.json"));

        var light = await _skin.GetStringAsync($"http://127.0.0.1:{_panelPort}/?tab=apps");
        var panel = await _http.GetStringAsync($"http://127.0.0.1:{_panelPort}/?tab=apps");

        Assert.Contains("Zed", light);
        Assert.Contains("Zed", panel);
    }

    [Fact]
    public async Task Speed_is_zero_while_the_tunnel_is_off()
    {
        var json = await _skin.GetStringAsync($"http://127.0.0.1:{_panelPort}/speed");
        Assert.Equal("{\"down\":0,\"up\":0}", json);
    }

    [Fact]
    public async Task A_login_page_wears_the_skin_too()
    {
        var cfg = CehoConfig.Load(Path.Combine(_root, "config.json"));
        Auth.SetPassword(cfg, "secret-pass-1");
        cfg.Save(Path.Combine(_root, "config.json"));

        var html = await _skin.GetStringAsync($"http://127.0.0.1:{_panelPort}/");
        Assert.Contains("class=light", html);
        Assert.Contains("name=password", html);
    }
}

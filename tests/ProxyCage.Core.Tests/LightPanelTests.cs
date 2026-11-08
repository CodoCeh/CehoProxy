using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class LightPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-light-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http = new();
    private readonly int _panelPort;
    private readonly int _lightPort;

    public LightPanelTests()
    {
        Directory.CreateDirectory(_root);
        new CehoConfig { PanelMode = CehoConfig.PanelModeSimple }.Save(Path.Combine(_root, "config.json"));
        _web = new WebServer(Path.Combine(_root, "config.json"), () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        _panelPort = TestPanel.Start(_web);
        _lightPort = Auth.ReadLightPointer(_root) ?? 0;
    }

    public void Dispose()
    {
        _web.Stop();
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Light_port_is_recorded_next_to_the_panel_port()
    {
        Assert.Equal(_panelPort, Auth.ReadPanelPointer(_root));
        Assert.True(_lightPort > _panelPort);
    }

    [Fact]
    public async Task Light_port_serves_the_android_skin_and_the_panel_port_does_not()
    {
        var light = await _http.GetStringAsync($"http://127.0.0.1:{_lightPort}/");
        var panel = await _http.GetStringAsync($"http://127.0.0.1:{_panelPort}/");

        Assert.Contains("<html lang=ru class=light>", light);
        Assert.Contains("CehoProxy Light", light);
        Assert.Contains("id=light-speed", light);
        Assert.Contains("class=\"light-vortex", light);
        Assert.DoesNotContain("class=light", panel);
        Assert.DoesNotContain("light-speed", panel);
    }

    [Fact]
    public async Task Both_ports_show_the_same_settings()
    {
        var cfg = CehoConfig.Load(Path.Combine(_root, "config.json"));
        cfg.Apps.Add(new AppEntry { Name = "Zed", Folder = "/Applications/Zed.app", Enabled = true });
        cfg.Save(Path.Combine(_root, "config.json"));

        var light = await _http.GetStringAsync($"http://127.0.0.1:{_lightPort}/?tab=apps");
        var panel = await _http.GetStringAsync($"http://127.0.0.1:{_panelPort}/?tab=apps");

        Assert.Contains("Zed", light);
        Assert.Contains("Zed", panel);
    }

    [Fact]
    public async Task Speed_is_zero_while_the_tunnel_is_off()
    {
        var json = await _http.GetStringAsync($"http://127.0.0.1:{_lightPort}/speed");
        Assert.Equal("{\"down\":0,\"up\":0}", json);
    }

    [Fact]
    public async Task A_login_page_on_the_light_port_wears_the_skin_too()
    {
        var cfg = CehoConfig.Load(Path.Combine(_root, "config.json"));
        Auth.SetPassword(cfg, "secret-pass-1");
        cfg.Save(Path.Combine(_root, "config.json"));

        var html = await _http.GetStringAsync($"http://127.0.0.1:{_lightPort}/");
        Assert.Contains("class=light", html);
        Assert.Contains("name=password", html);
    }
}

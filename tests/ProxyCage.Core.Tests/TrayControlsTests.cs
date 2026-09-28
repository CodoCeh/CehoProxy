using System.Net;

namespace ProxyCage.Core.Tests;

public class TrayControlsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-traybtn-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http;
    private string ConfigPath => Path.Combine(_root, "config.json");

    public TrayControlsTests()
    {
        Directory.CreateDirectory(_root);
        new CehoConfig { PanelMode = CehoConfig.PanelModeSimple }.Save(ConfigPath);

        _web = new WebServer(ConfigPath, () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        var port = TestPanel.Start(_web);
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
    public void A_fresh_install_keeps_the_icon_without_buttons() =>
        Assert.False(CehoConfig.Load(ConfigPath).TrayControls);

    [Fact]
    public async Task The_administrator_switches_the_buttons_in_the_panel()
    {
        Assert.Contains(Strings.T("ru", "tray_controls_off"), await _http.GetStringAsync("/"));

        await Toggle(on: true);
        Assert.True(CehoConfig.Load(ConfigPath).TrayControls);
        var page = await _http.GetStringAsync("/");
        Assert.Contains(Strings.T("ru", "tray_controls_on"), page);
        Assert.Contains(WebUtility.HtmlEncode(Strings.T("ru", "tray_controls_open")), page);

        await Toggle(on: false);
        Assert.False(CehoConfig.Load(ConfigPath).TrayControls);
    }

    private async Task Toggle(bool on)
    {
        var form = new Dictionary<string, string> { ["tab"] = "state" };
        if (on) form["enable"] = "1";
        using var reply = await _http.PostAsync("/tray-controls", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
    }
}

public class TrayControlsStateTests
{
    [Fact]
    public void The_icon_shows_buttons_only_when_the_service_allows_them()
    {
        const string allowed = """{"running":true,"daemon":true,"trayControls":true}""";
        const string silent = """{"running":true,"daemon":true}""";

        Assert.True(TrayState.ShowsControls(TrayState.Parse(allowed)));
        Assert.False(TrayState.ShowsControls(TrayState.Parse(silent)));
        Assert.False(TrayState.ShowsControls(null));
    }
}

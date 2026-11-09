using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("panel password")]
public class WhatsNewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-wn-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _config;
    private readonly WebServer _web;
    private readonly HttpClient _http = new();
    private readonly int _port;

    public WhatsNewTests()
    {
        Auth.ResetThrottle();
        Directory.CreateDirectory(_root);
        _config = Path.Combine(_root, "config.json");
        var cfg = new CehoConfig { PanelMode = CehoConfig.PanelModeSimple, WhatsNewSeen = "1.2.180" };
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "s", Url = "https://s.example/x" });
        cfg.Save(_config);
        _web = new WebServer(_config, () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        _port = TestPanel.Start(_web);
    }

    public void Dispose()
    {
        _web.Stop();
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static readonly WhatsNew.Release[] Fake =
    {
        new("1.0.1", new[] { new WhatsNew.Item("a", "a", "a", "a") }),
        new("1.0.2", new[] { new WhatsNew.Item("b", "b", "b", "b", "access") }),
        new("1.0.3", new[] { new WhatsNew.Item("c", "c", "c", "c") }),
    };

    [Fact]
    public void Since_returns_only_versions_between_seen_and_current_newest_first()
    {
        var items = WhatsNew.Since("1.0.1", "1.0.2", Fake);
        Assert.Equal(new[] { "b" }, items.Select(i => i.TitleRu));
        Assert.Equal(new[] { "c", "b", "a" }, WhatsNew.Since("0", "1.0.3", Fake).Select(i => i.TitleRu));
        Assert.Empty(WhatsNew.Since("1.0.3", "1.0.3", Fake));
    }

    [Fact]
    public void Fresh_install_sees_nothing_but_an_upgrade_without_a_marker_does()
    {
        Assert.Empty(WhatsNew.Pending(new CehoConfig(), "1.2.181"));
        var old = new CehoConfig();
        old.Subscriptions.Add(new SubscriptionEntry { Name = "s", Url = "https://s.example/x" });
        Assert.NotEmpty(WhatsNew.Pending(old, "1.2.181"));
    }

    [Fact]
    public async Task The_window_shows_once_then_stays_closed_after_it_is_marked_seen()
    {
        var url = $"http://127.0.0.1:{_port}";
        Assert.Contains("id=whatsnew", await _http.GetStringAsync(url + "/"));
        Assert.Contains("id=whatsnew", await _http.GetStringAsync(url + "/"));

        var reply = await _http.PostAsync(url + "/whatsnew/seen", new FormUrlEncodedContent(new Dictionary<string, string> { ["x"] = "1" }));
        Assert.True((int)reply.StatusCode < 400);

        Assert.DoesNotContain("id=whatsnew", await _http.GetStringAsync(url + "/"));
        Assert.Equal(Updater.CurrentVersion, CehoConfig.Load(_config).WhatsNewSeen);
    }

    [Fact]
    public async Task The_window_is_shown_in_light_too()
    {
        using var light = new HttpClient();
        light.DefaultRequestHeaders.Add("Cookie", "ceho-skin=light");
        Assert.Contains("id=whatsnew", await light.GetStringAsync($"http://127.0.0.1:{_port}/"));
    }
}

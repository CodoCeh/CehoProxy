using System.Net;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class AutoUpdateToggleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-autoupd-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http;
    private string ConfigPath => Path.Combine(_root, "config.json");

    public AutoUpdateToggleTests()
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
    public void A_fresh_install_does_not_update_itself() =>
        Assert.False(CehoConfig.Load(ConfigPath).AutoUpdate);

    [Fact]
    public async Task The_switch_is_written_to_the_config_and_can_be_taken_back()
    {
        await Toggle(on: true);
        Assert.True(CehoConfig.Load(ConfigPath).AutoUpdate);
        Assert.Contains(Strings.T("ru", "upd_auto_on"), await _http.GetStringAsync("/"));

        await Toggle(on: false);
        Assert.False(CehoConfig.Load(ConfigPath).AutoUpdate);
        Assert.Contains(Strings.T("ru", "upd_auto_off"), await _http.GetStringAsync("/"));
    }

    [Fact]
    public async Task The_panel_shows_the_version_that_changed_by_itself()
    {
        var cfg = CehoConfig.Load(ConfigPath);
        cfg.AutoUpdate = true;
        cfg.AutoUpdatedVersion = Updater.CurrentVersion;
        cfg.AutoUpdatedAtUtc = DateTime.UtcNow;
        cfg.Save(ConfigPath);

        Assert.Contains(
            Strings.T("ru", "upd_auto_done", Updater.CurrentVersion,
                cfg.AutoUpdatedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")),
            await _http.GetStringAsync("/"));
    }

    [Fact]
    public async Task A_note_about_an_older_version_does_not_linger()
    {
        var cfg = CehoConfig.Load(ConfigPath);
        cfg.AutoUpdatedVersion = "0.0.1";
        cfg.AutoUpdatedAtUtc = DateTime.UtcNow;
        cfg.Save(ConfigPath);

        Assert.DoesNotContain("0.0.1", await _http.GetStringAsync("/"));
    }

    private async Task Toggle(bool on)
    {
        var form = new Dictionary<string, string> { ["tab"] = "state" };
        if (on) form["enable"] = "1";
        using var reply = await _http.PostAsync("/autoupdate", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
    }
}

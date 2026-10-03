using System.Net;
using System.Text.RegularExpressions;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class SafetyPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-safety-panel-test-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_root, "config.json");
    private readonly WebServer _web;
    private readonly HttpClient _http;
    public SafetyPanelTests()
    {
        Directory.CreateDirectory(_root);
        new CehoConfig { Language = "en", PanelMode = CehoConfig.PanelModeSimple }.Save(ConfigPath);
        _web = new WebServer(ConfigPath, () => new(false, null, null, null, false), _ => { });
        var port = TestPanel.Start(_web);
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }
    public void Dispose()
    {
        _web.Stop(); _http.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task Simple_mode_warns_but_preserves_existing_insecure_setting_without_showing_a_toggle()
    {
        var cfg = CehoConfig.Load(ConfigPath);
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "test", Url = NaiveProxyHelper.BuildUri(new()
            { Enabled = true, Server = "example.test", Username = "fake-user", Password = "fake-password", AllowInsecure = true }) });
        cfg.Save(ConfigPath);
        var page = await _http.GetStringAsync("/?tab=subs");
        Assert.Contains("TLS verification disabled", page);
        Assert.Contains("TLS certificate verification is disabled", page);
        Assert.Contains("<input type=hidden name=allowInsecure value=1>", page);
        Assert.DoesNotContain("<input type=checkbox name=allowInsecure", page);
        Assert.Contains("data-confirm=", page);
        Assert.Contains("data-unsaved-confirm=", page);
        Assert.True(NaiveProxyHelper.TryParseUri(CehoConfig.Load(ConfigPath).Subscriptions[0].Url, out var saved));
        Assert.True(saved!.AllowInsecure);
    }

    [Fact]
    public async Task Advanced_mode_explains_what_insecure_setting_disables_and_its_risk()
    {
        var cfg = CehoConfig.Load(ConfigPath); cfg.PanelMode = CehoConfig.PanelModePro; cfg.Save(ConfigPath);
        var page = await _http.GetStringAsync("/?tab=subs");
        Assert.Contains("<details class=tls-advanced>", page);
        Assert.Contains("Disable TLS certificate verification (allowInsecure)", page);
        Assert.Contains("valid certificate is preferable", page);
        Assert.Contains("<input type=checkbox name=allowInsecure>", page);
    }

    [Fact]
    public async Task Cached_remote_subscription_displays_insecure_warning_without_fetching()
    {
        var cfg = CehoConfig.Load(ConfigPath);
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "cached", Url = "https://example.test/fake-sub" });
        cfg.Save(ConfigPath);
        File.WriteAllText(Path.Combine(_root, "sub-cached.txt"), NaiveProxyHelper.BuildUri(new()
            { Enabled = true, Server = "example.test", Username = "fake-user", Password = "fake-password", AllowInsecure = true }));
        var page = await _http.GetStringAsync("/?tab=subs");
        Assert.Contains("TLS verification disabled", page);
    }

    [Fact]
    public async Task Report_requires_local_preview_before_download_and_respects_section_selection()
    {
        var help = await _http.GetStringAsync("/?tab=help");
        Assert.Contains("action=/support/preview", help);
        using var response = await _http.PostAsync("/support/preview", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["report_environment"] = "1" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("[Environment]", html);
        Assert.DoesNotContain("[Configuration summary]", html);
        var match = Regex.Match(html, "name=report value=\"([^\"]*)\"");
        Assert.True(match.Success);
        var preview = WebUtility.HtmlDecode(match.Groups[1].Value);
        using var download = await _http.PostAsync("/support/download", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["report"] = preview }));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(preview, await download.Content.ReadAsStringAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("text/plain", download.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Restore_requires_explicit_confirmation_and_fresh_checkpoint_identity()
    {
        var calls = 0;
        _web.OnRestoreVerified = (_, _, _) => { Interlocked.Increment(ref calls); return Task.FromResult("restored"); };
        using var missingConfirmation = await _http.PostAsync("/settings/restore", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["tab"] = "access" }));
        Assert.Contains("e=1", missingConfirmation.Headers.Location!.ToString());
        using var missingIdentity = await _http.PostAsync("/settings/restore", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["tab"] = "access", ["confirm_restore"] = "1" }));
        Assert.Contains("e=1", missingIdentity.Headers.Location!.ToString());
        Assert.Equal(0, calls);
    }
}

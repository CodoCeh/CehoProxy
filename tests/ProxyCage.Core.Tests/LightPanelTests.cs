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

    private void Lock(string password)
    {
        var cfg = CehoConfig.Load(Path.Combine(_root, "config.json"));
        cfg.Apps.Add(new AppEntry { Name = "SecretApp", Folder = "/Applications/SecretApp.app", Enabled = true });
        Auth.SetPassword(cfg, password);
        cfg.Save(Path.Combine(_root, "config.json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?tab=apps")]
    [InlineData("?tab=sites")]
    [InlineData("?tab=subs")]
    [InlineData("?tab=exit")]
    [InlineData("?tab=doctor")]
    [InlineData("?tab=access")]
    [InlineData("?tab=browser")]
    [InlineData("?tab=log")]
    [InlineData("?tab=help")]
    public async Task With_a_password_every_light_page_shows_only_the_login(string query)
    {
        Lock("secret-pass-1");
        var html = await _skin.GetStringAsync($"http://127.0.0.1:{_panelPort}/{query}");

        Assert.Contains("name=password", html);
        Assert.Contains("class=light", html);
        Assert.DoesNotContain("SecretApp", html);
        Assert.DoesNotContain("id=light-speed", html);
        Assert.DoesNotContain("class=\"light-vortex", html);
    }

    [Fact]
    public async Task With_a_password_the_light_path_does_not_open_the_panel_and_speed_is_refused()
    {
        Lock("secret-pass-1");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler);
        var speed = await http.GetAsync($"http://127.0.0.1:{_panelPort}/speed");
        var landing = await _skin.GetStringAsync($"http://127.0.0.1:{_panelPort}/");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, speed.StatusCode);
        Assert.DoesNotContain("SecretApp", landing);
    }

    [Fact]
    public async Task With_a_password_commands_are_refused_without_a_session()
    {
        Lock("secret-pass-1");
        var before = File.ReadAllText(Path.Combine(_root, "config.json"));
        foreach (var path in new[] { "/apps/remove", "/subs/add", "/control/stop", "/mode", "/settings/save" })
        {
            var reply = await _skin.PostAsync($"http://127.0.0.1:{_panelPort}{path}",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "x", ["url"] = "https://example.invalid/", ["mode"] = "pro" }));
            var html = await reply.Content.ReadAsStringAsync();
            Assert.Contains("name=password", html);
        }
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "config.json")));
    }

    [Fact]
    public async Task A_wrong_password_is_rejected_and_the_right_one_opens_light()
    {
        Lock("secret-pass-1");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Add("Cookie", "ceho-skin=light");
        var url = $"http://127.0.0.1:{_panelPort}";

        var wrong = await http.PostAsync(url + "/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = "nope" }));
        var wrongHtml = await wrong.Content.ReadAsStringAsync();
        Assert.False(wrong.Headers.Contains("Set-Cookie"));
        Assert.Contains("name=password", wrongHtml);

        var right = await http.PostAsync(url + "/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = "secret-pass-1" }));
        var cookie = string.Join(";", right.Headers.GetValues("Set-Cookie")).Split(';')[0];
        Assert.StartsWith("ceho=", cookie);

        using var inside = new HttpRequestMessage(HttpMethod.Get, url + "/");
        inside.Headers.Add("Cookie", "ceho-skin=light; " + cookie);
        var page = await (await http.SendAsync(inside)).Content.ReadAsStringAsync();
        Assert.Contains("SecretApp", await GetWith(http, url + "/?tab=apps", cookie));
        Assert.Contains("id=light-speed", page);
        Assert.DoesNotContain("<div class=gate>", page);

        using var fast = new HttpRequestMessage(HttpMethod.Get, url + "/speed");
        fast.Headers.Add("Cookie", cookie);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await http.SendAsync(fast)).StatusCode);
    }

    private static async Task<string> GetWith(HttpClient http, string url, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", "ceho-skin=light; " + cookie);
        return await (await http.SendAsync(request)).Content.ReadAsStringAsync();
    }
}

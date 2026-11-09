using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("panel password")]
public class OpenTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-open-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _config;
    private readonly WebServer _web;
    private readonly HttpClient _http = new();
    private readonly int _port;

    public OpenTargetTests()
    {
        Auth.ResetThrottle();
        Directory.CreateDirectory(_root);
        _config = Path.Combine(_root, "config.json");
        new CehoConfig { PanelMode = CehoConfig.PanelModeSimple }.Save(_config);
        _web = new WebServer(_config, () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        _port = TestPanel.Start(_web);
    }

    public void Dispose()
    {
        _web.Stop();
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Url(string path) => $"http://127.0.0.1:{_port}{path}";

    private static FormUrlEncodedContent Form(params (string, string)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, string>(p.Item1, p.Item2)));

    [Theory]
    [InlineData(null, null, "ask")]
    [InlineData("web", "light", "web")]
    [InlineData("light", "web", "light")]
    [InlineData(null, "light", "light")]
    [InlineData(null, "web", "web")]
    [InlineData("ask", "light", "ask")]
    [InlineData("nonsense", "web", "web")]
    [InlineData(null, "nonsense", "ask")]
    public void The_command_word_beats_the_stored_choice_and_unknown_words_mean_ask(string? asked, string? stored, string expected) =>
        Assert.Equal(expected, PanelLauncher.ResolveTarget(asked, stored));

    [Fact]
    public void The_choice_travels_next_to_the_port_and_old_pointers_still_read()
    {
        Assert.Equal(CehoConfig.OpenAsk, Auth.ReadOpenTarget(_root));
        Assert.Equal(_port, Auth.ReadPanelPointer(_root));
        Assert.Equal(_port, Auth.ReadLightPointer(_root));

        File.WriteAllText(Path.Combine(_root, "panel.port"), "8899 2080");
        Assert.Null(Auth.ReadOpenTarget(_root));
        Assert.Equal(8899, Auth.ReadPanelPointer(_root));
        File.WriteAllText(Path.Combine(_root, "panel.port"), "8899 2080 8899");
        Assert.Null(Auth.ReadOpenTarget(_root));
        File.WriteAllText(Path.Combine(_root, "panel.port"), "8899 2080 8899 light");
        Assert.Equal("light", Auth.ReadOpenTarget(_root));
        Assert.Equal(2080, Auth.ReadProxyPointer(_root));
    }

    [Fact]
    public async Task The_chooser_page_offers_both_versions_a_remember_box_and_the_nonce()
    {
        var html = await _http.GetStringAsync(Url("/open?n=abcdef012345"));

        Assert.Contains("data-target=light", html);
        Assert.Contains("data-target=web", html);
        Assert.Contains("id=remember", html);
        Assert.Contains("\"abcdef012345\"", html);
        Assert.DoesNotContain("name=password", html);
    }

    [Fact]
    public async Task The_chooser_works_before_sign_in_but_shows_nothing_of_the_panel()
    {
        var cfg = CehoConfig.Load(_config);
        cfg.Apps.Add(new AppEntry { Name = "SecretApp", Folder = "/Applications/SecretApp.app", Enabled = true });
        Auth.SetPassword(cfg, "secret-pass-1");
        cfg.Save(_config);

        var html = await _http.GetStringAsync(Url("/open?n=abcdef012345"));

        Assert.Contains("data-target=light", html);
        Assert.DoesNotContain("SecretApp", html);
        Assert.DoesNotContain("class=gate", html);
    }

    [Fact]
    public async Task A_pick_is_handed_over_once_and_remember_saves_it_for_next_time()
    {
        var pick = await _http.PostAsync(Url("/open-pick"), Form(("n", "abcdef012345"), ("target", "light"), ("remember", "1")));

        Assert.Equal(System.Net.HttpStatusCode.NoContent, pick.StatusCode);
        Assert.Equal("light", CehoConfig.Load(_config).OpenTarget);
        Assert.Equal("light", Auth.ReadOpenTarget(_root));
        Assert.Equal("light", await _http.GetStringAsync(Url("/open-pick?n=abcdef012345")));
        Assert.Equal("", await _http.GetStringAsync(Url("/open-pick?n=abcdef012345")));
        Assert.Equal("", await _http.GetStringAsync(Url("/open-pick?n=someoneelse1")));
    }

    [Fact]
    public async Task Without_the_box_ticked_the_pick_is_used_once_and_nothing_is_saved()
    {
        await _http.PostAsync(Url("/open-pick"), Form(("n", "abcdef012345"), ("target", "web")));

        Assert.Equal("web", await _http.GetStringAsync(Url("/open-pick?n=abcdef012345")));
        Assert.Equal(CehoConfig.OpenAsk, CehoConfig.NormalizeOpenTarget(CehoConfig.Load(_config).OpenTarget));
        Assert.Equal(CehoConfig.OpenAsk, Auth.ReadOpenTarget(_root));
    }

    [Theory]
    [InlineData("abcdef012345", "nonsense")]
    [InlineData("abc", "light")]
    [InlineData("", "web")]
    public async Task A_pick_with_a_bad_target_or_code_is_refused(string nonce, string target)
    {
        var pick = await _http.PostAsync(Url("/open-pick"), Form(("n", nonce), ("target", target), ("remember", "1")));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, pick.StatusCode);
        Assert.Equal(CehoConfig.OpenAsk, CehoConfig.NormalizeOpenTarget(CehoConfig.Load(_config).OpenTarget));
    }

    [Fact]
    public async Task The_settings_form_saves_the_choice_and_both_skins_show_it()
    {
        using var skin = new HttpClient();
        skin.DefaultRequestHeaders.Add("Cookie", "ceho-skin=light");

        await _http.PostAsync(Url("/open-target"), Form(("open", "light"), ("tab", "access")));

        Assert.Equal("light", CehoConfig.Load(_config).OpenTarget);
        Assert.Equal("light", Auth.ReadOpenTarget(_root));
        foreach (var client in new[] { _http, skin })
        {
            var page = await client.GetStringAsync(Url("/?tab=access"));
            Assert.Contains("action=/open-target", page);
            Assert.Contains("<option value=light selected>", page);
        }

        await _http.PostAsync(Url("/open-target"), Form(("open", "garbage"), ("tab", "access")));
        Assert.Equal(CehoConfig.OpenAsk, CehoConfig.NormalizeOpenTarget(CehoConfig.Load(_config).OpenTarget));
    }

    [Fact]
    public async Task With_a_password_the_choice_cannot_be_changed_from_the_settings_without_signing_in()
    {
        var cfg = CehoConfig.Load(_config);
        Auth.SetPassword(cfg, "secret-pass-1");
        cfg.Save(_config);

        var reply = await _http.PostAsync(Url("/open-target"), Form(("open", "web"), ("tab", "access")));
        var html = await reply.Content.ReadAsStringAsync();

        Assert.Contains("name=password", html);
        Assert.Equal(CehoConfig.OpenAsk, CehoConfig.NormalizeOpenTarget(CehoConfig.Load(_config).OpenTarget));
    }
}

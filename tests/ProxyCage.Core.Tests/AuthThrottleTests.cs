using System.Net;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("panel password")]
public class AuthThrottleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-auth-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http;
    private string ConfigPath => Path.Combine(_root, "config.json");

    public AuthThrottleTests()
    {
        Auth.ResetThrottle();
        Directory.CreateDirectory(_root);
        var cfg = new CehoConfig();
        Auth.SetPassword(cfg, "tajna123");
        cfg.Save(ConfigPath);

        _web = new WebServer(ConfigPath, () => new WebServer.ControlState(false, null, null, null, false), _ => { })
        {
            OnApiCommand = argv => Task.FromResult((true, string.Join(" ", argv))),
        };
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
        Auth.ResetThrottle();
        try { Directory.Delete(_root, true); } catch { }
    }

    private CehoConfig Config => CehoConfig.Load(ConfigPath);

    [Fact]
    public void Three_wrong_tries_pass_then_the_door_closes_for_a_growing_while()
    {
        for (var i = 0; i < 3; i++)
            Assert.False(Auth.Check(Config, "wrong").Locked);

        var first = Auth.Check(Config, "wrong");
        Assert.True(first.Locked);
        Assert.Equal(5, first.RetrySeconds);
    }

    [Fact]
    public void A_closed_door_turns_away_even_the_right_password()
    {
        for (var i = 0; i < 4; i++) Auth.Check(Config, "wrong");

        var check = Auth.Check(Config, "tajna123");
        Assert.False(check.Ok);
        Assert.True(check.Locked);
    }

    [Fact]
    public void A_good_password_clears_the_count()
    {
        for (var i = 0; i < 3; i++) Auth.Check(Config, "wrong");
        Assert.True(Auth.Check(Config, "tajna123").Ok);

        Assert.False(Auth.Check(Config, "wrong").Locked);
    }

    [Fact]
    public void An_empty_password_is_a_question_not_an_attempt()
    {
        for (var i = 0; i < 10; i++)
        {
            var check = Auth.Check(Config, "");
            Assert.False(check.Ok);
            Assert.False(check.Locked);
        }

        Assert.True(Auth.Check(Config, "tajna123").Ok);
    }

    [Fact]
    public void Without_a_password_nothing_is_counted()
    {
        var open = new CehoConfig();
        for (var i = 0; i < 10; i++) Assert.True(Auth.Check(open, "whatever").Ok);
    }

    [Fact]
    public async Task The_panel_says_how_long_the_door_stays_closed()
    {
        for (var i = 0; i < 3; i++)
        {
            using var early = await Login("wrong");
            Assert.Contains(Strings.T("ru", "auth_wrong"), await early.Content.ReadAsStringAsync());
        }

        using var late = await Login("wrong");
        Assert.Contains(Strings.T("ru", "auth_locked", 5), await late.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_cli_probe_without_a_password_never_closes_the_door()
    {
        for (var i = 0; i < 6; i++)
        {
            using var probe = await Api("");
            Assert.Equal(HttpStatusCode.Unauthorized, probe.StatusCode);
        }

        using var good = await Api("tajna123");
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }

    [Fact]
    public async Task The_api_answers_a_closed_door_with_too_many_requests()
    {
        for (var i = 0; i < 3; i++)
        {
            using var early = await Api("wrong");
            Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);
        }

        using var late = await Api("wrong");
        Assert.Equal(HttpStatusCode.TooManyRequests, late.StatusCode);
        Assert.Equal("5", late.Headers.GetValues("Retry-After").Single());
    }

    private Task<HttpResponseMessage> Login(string password) =>
        _http.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = password,
        }));

    private Task<HttpResponseMessage> Api(string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api")
        {
            Content = new StringContent("status", System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.TryAddWithoutValidation("X-Ceho-Password", password);
        return _http.SendAsync(request);
    }
}

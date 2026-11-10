using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("panel password")]
public class OnboardingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-ob-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly HttpClient _http = new();
    private readonly HttpClient _skin = new();
    private readonly int _port;

    public OnboardingTests()
    {
        Auth.ResetThrottle();
        Directory.CreateDirectory(_root);
        new CehoConfig { PanelMode = CehoConfig.PanelModeSimple }.Save(Path.Combine(_root, "config.json"));
        _web = new WebServer(Path.Combine(_root, "config.json"), () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        _port = TestPanel.Start(_web);
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
    public async Task A_new_profile_shows_the_wizard_with_an_animated_scene_right_under_the_status()
    {
        var html = await _http.GetStringAsync($"http://127.0.0.1:{_port}/");

        var wizard = html.IndexOf("class=wizard", StringComparison.Ordinal);
        var apps = html.IndexOf("id=added-apps", StringComparison.Ordinal);
        Assert.True(wizard > 0);
        Assert.True(apps < 0 || wizard < apps, "мастер должен стоять выше списка программ");
        Assert.Contains("class=\"ob-svg ob-sub\"", html);
        Assert.Contains("role=\"img\"", html);
    }

    [Fact]
    public async Task Light_without_a_subscription_asks_to_add_one_before_pressing_the_vortex()
    {
        var html = await _skin.GetStringAsync($"http://127.0.0.1:{_port}/");

        Assert.Contains("Сначала добавьте подписку", html);
        Assert.DoesNotContain("Нажмите на водоворот, чтобы подключить", html);
        Assert.Contains("ob-sub", html);
    }

    [Fact]
    public async Task The_help_tab_tells_the_four_steps_with_scenes_and_captions()
    {
        var html = await _http.GetStringAsync($"http://127.0.0.1:{_port}/?tab=help");

        foreach (var kind in new[] { "ob-sub", "ob-apps", "ob-power", "ob-check" })
            Assert.Contains($"class=\"ob-svg {kind}\"", html);
        foreach (var caption in new[] { "1. Вставьте ссылку", "2. Выберите программы", "3. Включите", "4. Перезапустите программу" })
            Assert.Contains(caption, html);
    }

    [Fact]
    public void The_animation_stops_for_people_who_ask_for_less_motion_and_needs_no_script()
    {
        Assert.Contains("@media (prefers-reduced-motion:no-preference)", OnboardingUi.Css);
        Assert.DoesNotContain("<script", OnboardingUi.Css, StringComparison.OrdinalIgnoreCase);
        var scene = new System.Text.StringBuilder();
        OnboardingUi.Scene(scene, "power", (key, _) => key);
        Assert.DoesNotContain("<script", scene.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", scene.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

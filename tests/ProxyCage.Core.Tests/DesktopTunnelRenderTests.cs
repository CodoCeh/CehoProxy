using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Production HTML with an explicitly injected synthetic app catalog and traffic.
/// Calls the real apply admission method with an in-memory callback, never a
/// listener, engine, process restart, firewall operation, or network probe.
/// </summary>
[Collection("verified-panel")]
public sealed class DesktopTunnelRenderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ceho-tunnel-fixture-").FullName;
    private string ConfigPath => Path.Combine(_root, "config.json");
    private readonly CehoConfig _cfg;
    private readonly WebServer _web;
    private WebServer.ControlState _state = new(true, "NL", "203.0.113.9", null, true);
    private IReadOnlyList<WebServer.AppLive> _samples = Array.Empty<WebServer.AppLive>();
    private readonly IReadOnlyList<InstalledAppCatalog.Entry> _catalog;
    private int _applyCalls;

    public DesktopTunnelRenderTests()
    {
        _catalog = new[]
        {
            new InstalledAppCatalog.Entry("Fixture Browser", Path.Combine(_root, "browser", "browser.exe"), "fixture"),
            new InstalledAppCatalog.Entry("Fixture Notes", Path.Combine(_root, "notes", "notes.exe"), "fixture"),
            new InstalledAppCatalog.Entry("Fixture Editor Alpha", Path.Combine(_root, "alpha", "editor.exe"), "fixture"),
            new InstalledAppCatalog.Entry("Fixture Editor Beta", Path.Combine(_root, "beta", "editor.exe"), "fixture"),
            new InstalledAppCatalog.Entry("Fixture <Unsafe & Quoted> Tool", Path.Combine(_root, "unsafe", "tool.exe"), "fixture"),
        };
        _cfg = new CehoConfig
        {
            Language = "en", PanelMode = CehoConfig.PanelModeSimple,
            Apps = { new AppEntry { Name = "Fixture Browser", Folder = _catalog[0].Path, SingleFile = true } },
        };
        _cfg.Save(ConfigPath);
        _web = new WebServer(ConfigPath, () => _state, _ => { })
        {
            OnAppsLive = () => _samples,
            PanelPreflight = _ => Array.Empty<Preflight.Check>(),
            InstalledApps = _ => _catalog,
            DetectedTools = () => Array.Empty<AiTools.Found>(),
            RunningBrowsers = _ => Array.Empty<string>(),
            OnApplyWithRestartConfirmation = (_, _) =>
            {
                _applyCalls++;
                return Task.FromResult("Fixture rules applied; no engine was started");
            },
        };
    }

    private void Set(string name, object value) => typeof(WebServer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_web, value);
    private string Render(Job? job = null)
    {
        var method = typeof(WebServer).GetMethod("RenderPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var values = new Dictionary<string, object?>
        {
            ["cfg"] = _cfg, ["st"] = _state, ["tab"] = "apps", ["flash"] = null, ["flashErr"] = false,
            ["job"] = job, ["logView"] = LogView.All, ["tunnelFolder"] = null, ["wizard"] = null,
        };
        return (string)method.Invoke(_web, method.GetParameters().Select(parameter =>
            values.TryGetValue(parameter.Name!, out var value) ? value : parameter.HasDefaultValue ? parameter.DefaultValue :
                throw new InvalidOperationException("Unknown RenderPage argument: " + parameter.Name)).ToArray())!;
    }

    private async Task ApplyFixture()
    {
        _cfg.Save(ConfigPath);
        var method = typeof(WebServer).GetMethod("ApplyPostAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<(string? Message, bool IsError, string? JobId)>)method.Invoke(_web,
            new object[] { "/apply", new Dictionary<string, string> { ["confirm_apply"] = "1" }, CehoConfig.Load(ConfigPath) })!;
        Assert.False(result.IsError, result.Message);
        Assert.NotNull(result.JobId);
        var job = Jobs.Find(result.JobId);
        Assert.NotNull(job);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (job.Running && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.Equal(JobState.Done, job.State);
        Assert.True(_applyCalls > 0);
    }

    private void Samples(params WebServer.AppLive[] samples)
    {
        _samples = samples;
        typeof(WebServer).GetMethod("InvalidateAppObservations", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_web, null);
    }

    [Fact]
    public void Catalog_is_synthetic_accessible_and_escaped_without_inventing_a_file_path()
    {
        var html = Render();
        Assert.Contains("id=tunnel-workspace", html);
        Assert.Contains("id=tunnel-stage", html);
        Assert.Contains("id=tunnel-confirm", html);
        Assert.Contains("data-tunnel-source", html);
        Assert.Contains(WebUtility.HtmlEncode(_catalog[1].Path), html);
        Assert.Contains("Fixture &lt;Unsafe &amp; Quoted&gt; Tool", html);
        Assert.DoesNotContain("Fixture <Unsafe & Quoted> Tool", html);
        Assert.Contains("name=confirm_add", html);
        Assert.Contains("aria-live=polite", html);
    }

    [Fact]
    public async Task Actual_apply_acknowledgment_is_separate_from_observed_traffic()
    {
        Assert.False(_web.IsAppRuleApplied(_cfg.Apps[0]));
        Set("_pending", 1);
        Samples(new WebServer.AppLive(_cfg.Apps[0].Folder, 1, 2, 0, 2));
        var pending = Render();
        Assert.Contains("data-rule-state=\"pending\"", pending);
        Assert.Contains("data-observation=\"pending-rules\"", pending);
        Assert.False(_web.IsAppRuleApplied(_cfg.Apps[0]));
        Samples(new WebServer.AppLive(_cfg.Apps[0].Folder, 1, 0, 0));
        await ApplyFixture();
        var applied = Render();
        Assert.True(_web.IsAppRuleApplied(_cfg.Apps[0]));
        Assert.Contains("data-rule-state=\"applied\"", applied);
        Assert.Contains("data-observation=\"quiet\"", applied);
        Assert.DoesNotContain("data-observation=\"vpn\"", applied);
        Samples(new WebServer.AppLive(_cfg.Apps[0].Folder, 1, 2, 0, 2));
        var observed = Render();
        Assert.Contains("data-observation=\"vpn\"", observed);
    }

    [Fact]
    public void Successful_unrelated_job_does_not_claim_rules_applied()
    {
        var unrelated = new Job { Id = "fixture-unrelated", Kind = "doctor", Title = "Fixture unrelated check" };
        unrelated.Complete("Fixture succeeded");
        var html = Render(unrelated);
        Assert.False(_web.IsAppRuleApplied(_cfg.Apps[0]));
        Assert.DoesNotContain("data-rule-state=\"applied\"", html);
    }

    [Fact]
    public async Task Fixture_pages_can_be_exported_for_visual_review()
    {
        var output = Environment.GetEnvironmentVariable("CEHO_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        foreach (var language in new[] { "en", "ru" })
        {
            _cfg.Language = language;
            Set("_pending", 0);
            _cfg.Apps.RemoveAll(app => app.Name != "Fixture Browser");
            _cfg.Save(ConfigPath);
            Samples(new WebServer.AppLive(_cfg.Apps[0].Folder, 1, 0, 0));
            void Save(string name)
            {
                var label = language == "ru" ? "Пример интерфейса · демонстрационные данные · реальный VPN не запускается"
                    : "UI fixture · example data · no real VPN is started";
                var html = Regex.Replace(Render(), "<body([^>]*)>", match => match.Value
                    + "<aside class=fixture-banner style=\"text-align:center;padding:8px;font-size:12px\" role=note>"
                    + WebUtility.HtmlEncode(label) + "</aside>", RegexOptions.CultureInvariant);
                File.WriteAllText(Path.Combine(output, name + "-" + language + ".html"), html);
                File.WriteAllText(Path.Combine(output, name + "-" + language + ".json"),
                    System.Text.Json.JsonSerializer.Serialize(_web.TunnelState(_cfg)));
            }
            Save("tunnel-ready");
            Save("tunnel-confirmation"); // The browser opens the actual confirmation dialog.
            _cfg.Apps.Add(new AppEntry { Name = "Fixture Notes", Folder = _catalog[1].Path, SingleFile = true });
            _cfg.Save(ConfigPath);
            Set("_pending", 1);
            Save("tunnel-pending");
            await ApplyFixture();
            Samples(_cfg.Apps.Select(app => new WebServer.AppLive(app.Folder, 1, 0, 0)).ToArray());
            Save("tunnel-applied");
            Samples(_cfg.Apps.Select(app => new WebServer.AppLive(app.Folder, 1, 2, 0, 2)).ToArray());
            Save("tunnel-observed");
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}

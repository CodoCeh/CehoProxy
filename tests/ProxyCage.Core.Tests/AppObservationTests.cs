using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;

namespace ProxyCage.Core.Tests;

public sealed class AppObservationTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 1, 0, 0, DateTimeKind.Utc);
    private static readonly CehoConfig Config = new() { Language = "en" };
    private static readonly AppEntry App = new() { Name = "Example", Folder = "/fixture/app" };

    [Theory]
    [InlineData(0, 0, 0, 0, 0, "idle", false, false)]
    [InlineData(1, 0, 0, 0, 0, "quiet", false, false)]
    [InlineData(1, 1, 0, 0, 0, "tunnel", false, false)]
    [InlineData(1, 4, 0, 0, 4, "direct-rule", false, false)]
    [InlineData(1, 0, 0, 1, 0, "vpn", true, false)]
    [InlineData(0, 0, 1, 0, 0, "leak", false, true)]
    [InlineData(1, 8, 1, 8, 0, "leak", false, true)]
    [InlineData(1, 0, 0, 0, 2, "direct-rule", false, false)]
    [InlineData(1, 3, 0, 3, 2, "vpn", true, false)]
    public void Observed_traffic_is_distinct_from_configured_route(int processes, int tunnel, int direct,
        int engineVpn, int engineDirect, string kind, bool verified, bool leak)
    {
        var live = new WebServer.AppLive(App.Folder, processes, tunnel, direct, engineVpn, engineDirect);
        var result = AppObservation.Evaluate(Config, App, live, true, true, Now, Now);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(verified, result.Verified);
        Assert.Equal(leak, result.Leak);
        if (kind is "quiet" or "idle") Assert.Equal("off", result.Css);
    }

    [Fact]
    public void No_observation_is_not_a_success()
    {
        var result = AppObservation.Evaluate(Config, App, null, true, true, Now, Now);
        Assert.Equal("unknown", result.Kind);
        Assert.False(result.Verified);
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void Old_failed_or_future_observations_cannot_verify(int age, bool failed)
    {
        var result = AppObservation.Evaluate(Config, App, new(App.Folder, 1, 2, 0), true, true,
            Now.AddSeconds(-age), Now, failed);
        Assert.Equal("stale", result.Kind);
        Assert.False(result.Verified);
        Assert.False(result.Fresh);
    }

    [Fact]
    public void Stale_leak_does_not_become_a_current_leak_or_disappear()
    {
        var result = AppObservation.Evaluate(Config, App, new(App.Folder, 1, 0, 2), true, true,
            Now.AddMinutes(-1), Now);
        Assert.False(result.Leak);
        Assert.Equal("warn", result.Css);
        Assert.Contains("Direct traffic was found", result.Advice);
    }

    [Fact]
    public void Paused_tunnel_cannot_reuse_a_successful_sample()
    {
        var result = AppObservation.Evaluate(Config, App, new(App.Folder, 1, 2, 0), false, true, Now, Now);
        Assert.False(result.Verified);
        Assert.Equal("stopped", result.Kind);
        Assert.Contains("not a check", result.Advice);
    }

    [Fact]
    public void Offline_rule_is_not_verified_by_no_traffic_and_reports_unexpected_traffic()
    {
        var app = new AppEntry { NoInternet = true, Folder = App.Folder };
        var quiet = AppObservation.Evaluate(Config, app, new(App.Folder, 1, 0, 0), true, true, Now, Now);
        Assert.Equal("blocked", quiet.Kind);
        Assert.False(quiet.Verified);
        var connected = AppObservation.Evaluate(Config, app, new(App.Folder, 1, 1, 0), true, true, Now, Now);
        Assert.Equal("blocked-traffic", connected.Kind);
        Assert.True(connected.Leak);
    }

    [Fact]
    public void Disabled_rule_does_not_inherit_a_successful_sample()
    {
        var app = new AppEntry { Enabled = false, Folder = App.Folder };
        var result = AppObservation.Evaluate(Config, app, new(App.Folder, 1, 2, 0), true, true, Now, Now);
        Assert.Equal("disabled", result.Kind);
        Assert.False(result.Verified);
    }

    [Fact]
    public void Subscription_requires_successful_recent_nonempty_unexpired_result()
    {
        var sub = new SubscriptionEntry { LastCheckOk = true, LastNodes = 3, LastCheckedUtc = Now.ToString("O") };
        Assert.True(AppObservation.SubscriptionVerified(sub, Now));
        sub.LastCheckedUtc = Now.AddDays(-2).ToString("O");
        Assert.False(AppObservation.SubscriptionVerified(sub, Now));
        sub.LastCheckedUtc = Now.ToString("O"); sub.ExpiresUtc = Now.AddDays(-1);
        Assert.False(AppObservation.SubscriptionVerified(sub, Now));
        sub.ExpiresUtc = null; sub.LastNodes = 0;
        Assert.False(AppObservation.SubscriptionVerified(sub, Now));
        sub.LastNodes = 3; sub.LastCheckOk = false;
        Assert.False(AppObservation.SubscriptionVerified(sub, Now));
        sub.LastCheckOk = true; sub.Enabled = false;
        Assert.False(AppObservation.SubscriptionVerified(sub, Now));
    }

    [Fact]
    public void Unapplied_rules_cannot_be_verified_by_old_route_traffic()
    {
        var result = AppObservation.Evaluate(Config, App, new(App.Folder, 1, 2, 0, 2), true, true,
            Now, Now, rulesPending: true);
        Assert.False(result.Verified);
        Assert.Equal("pending-rules", result.Kind);
    }

    [Fact]
    public void Windows_stop_description_explains_persistent_guard()
    {
        var text = AppObservation.StopConsequence(Config, true, true);
        Assert.Contains("Windows guard rules remain", text);
        Assert.Contains("does not enable direct access", text);
    }
}

/// <summary>Fixture-only render tests. No listener, VPN, firewall, network probe or repair is run.</summary>
[CollectionDefinition("verified-panel", DisableParallelization = true)]
public sealed class VerifiedPanelCollection { }

[Collection("verified-panel")]
public sealed class VerifiedPanelRenderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-render-safe-" + Guid.NewGuid().ToString("N"));
    private readonly CehoConfig _cfg;
    private readonly WebServer _web;
    private WebServer.ControlState _state = new(true, "NL", "203.0.113.9", null, true);
    private IReadOnlyList<WebServer.AppLive> _samples;

    public VerifiedPanelRenderTests()
    {
        Directory.CreateDirectory(_root);
        _cfg = new CehoConfig
        {
            Language = "en", PanelMode = CehoConfig.PanelModeSimple,
            Apps = { new AppEntry { Name = "Chrome", Folder = Path.Combine(_root, "Chrome") } },
            Subscriptions = { new SubscriptionEntry { Name = "Test subscription", Url = "https://example.invalid/sub",
                LastCheckOk = true, LastNodes = 3, LastCheckedUtc = DateTime.UtcNow.ToString("O") } },
        };
        _cfg.Save(Path.Combine(_root, "config.json"));
        _samples = new[] { new WebServer.AppLive(_cfg.Apps[0].Folder, 1, 2, 0, 2, 0) };
        _web = new WebServer(Path.Combine(_root, "config.json"), () => _state, _ => { })
        {
            OnAppsLive = () => _samples,
        };
        Set("_doctor", new Doctor.Result(Array.Empty<Preflight.Check>(), Array.Empty<string>(), Array.Empty<string>()));
        Set("_doctorAtUtc", DateTime.UtcNow);
        Set("_doctorSimple", true);
    }

    private void Set(string name, object value) => typeof(WebServer).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_web, value);
    private object? Call(string name, params object?[] args) => typeof(WebServer).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_web, args);
    private string Render(string tab = "state", string? wizard = null, Job? job = null) => (string)Call("RenderPage", _cfg, _state, tab, null, false, job,
        LogView.All, null, wizard, null)!;
    private string RenderMethod(string name, params object?[] extra)
    {
        var sb = new StringBuilder();
        Func<string, object[], string> translate = (k, a) => Strings.T(_cfg.Language, k, _cfg.SimplePanel, a);
        if (name == "RenderWizard") Call(name, sb, _cfg, _state, 3, translate);
        else Call(name, sb, _cfg, translate);
        return sb.ToString();
    }
    private void Refresh() => Call("InvalidateAppObservations");
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Exit_ip_does_not_hide_a_real_app_leak()
    {
        _samples = new[] { new WebServer.AppLive(_cfg.Apps[0].Folder, 1, 1, 2) };
        var html = Render();
        Assert.Contains("<h1>Direct traffic detected</h1>", html);
        Assert.Contains("data-observation=\"leak\"", html);
        Assert.DoesNotContain("<h1>Tunnel connected</h1>", html);
    }

    [Fact]
    public void Quiet_app_is_neutral_and_not_counted_as_verified()
    {
        _samples = new[] { new WebServer.AppLive(_cfg.Apps[0].Folder, 1, 0, 0) };
        var html = Render();
        Assert.Contains("VPN traffic observed for 0 of 1 apps", html);
        Assert.Contains("observation-badge off\" data-observation=\"quiet", html);
    }

    [Fact]
    public void Failed_refresh_keeps_original_timestamp_and_marks_cached_result_stale()
    {
        Render();
        var before = (DateTime)typeof(WebServer).GetField("_appsLiveAtUtc", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_web)!;
        Set("_appsLiveAttemptAtUtc", DateTime.UtcNow.AddSeconds(-20));
        _web.OnAppsLive = () => throw new InvalidOperationException("fixture unavailable");
        var html = Render();
        var after = (DateTime)typeof(WebServer).GetField("_appsLiveAtUtc", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_web)!;
        Assert.Equal(before, after);
        Assert.Contains("data-observation=\"stale\"", html);
        Assert.Contains("VPN traffic observed for 0 of 1 apps", html);
    }

    [Fact]
    public void Wizard_waits_for_app_traffic_and_offers_retry_without_done_button()
    {
        _samples = new[] { new WebServer.AppLive(_cfg.Apps[0].Folder, 0, 0, 0) };
        var html = RenderMethod("RenderWizard");
        Assert.Contains("Waiting for connection verification", html);
        Assert.Contains("action=/apps/check", html);
        Assert.Contains("name=wizard value=\"3\"", html);
        Assert.DoesNotContain("Connection verified</b>", html);
        Assert.DoesNotContain("href=\"/?tab=state\"", html);
    }

    [Fact]
    public void Wizard_requires_all_three_checks_and_then_can_finish()
    {
        var ready = RenderMethod("RenderWizard");
        Assert.Contains("Connection verified</b>", ready);
        _cfg.Subscriptions[0].LastCheckOk = null;
        var pending = RenderMethod("RenderWizard");
        Assert.DoesNotContain("Connection verified</b>", pending);
        Assert.Contains("action=/subs/check", pending);
        _cfg.Subscriptions[0].LastCheckOk = true;
        _state = _state with { Probed = false };
        Assert.DoesNotContain("Connection verified</b>", RenderMethod("RenderWizard"));
    }

    [Fact]
    public void Doctor_offers_app_recheck_and_does_not_claim_global_protection()
    {
        var html = RenderMethod("RenderDoctor");
        Assert.Contains("action=/apps/check", html);
        Assert.Contains("name=tab value=\"doctor\"", html);
        Assert.Contains("General checks found no issues", html);
        Assert.Contains("Helper processes are counted", html);
    }

    [Fact]
    public void Apps_keep_management_controls_and_escape_observed_content()
    {
        _cfg.Apps[0].DisplayName = "<script>alert(1)</script>";
        var html = Render("apps");
        Assert.Contains("class=app-cards", html);
        Assert.Contains("Configured:", html);
        Assert.Contains("action=/apps/remove", html);
        Assert.Contains("action=/apps/rename", html);
        Assert.Contains("tunnel=", html);
        Assert.Contains("Last sample:", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Current_preflight_blocker_is_included_even_when_cached_doctor_was_healthy()
    {
        _web.PanelPreflight = _ => new[] { new Preflight.Check(Preflight.Level.Blocker, "Fixture blocker", null, null) };
        var html = RenderMethod("RenderCheckSummary");
        Assert.Contains("line bad", html);
        Assert.DoesNotContain("General checks found no issues", html);
    }

    [Fact]
    public void Pending_routes_cannot_complete_wizard()
    {
        Set("_pending", 1);
        Assert.DoesNotContain("Connection verified</b>", RenderMethod("RenderWizard"));
        Assert.Contains("data-observation=\"pending-rules\"", Render("apps"));
    }

    [Fact]
    public void Config_change_invalidates_diagnostic_freshness_without_running_a_probe()
    {
        Assert.True((bool)Call("DoctorResultIsFresh", _cfg)!);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "config.json"), DateTime.UtcNow.AddSeconds(1));
        Assert.False((bool)Call("DoctorResultIsFresh", _cfg)!);
    }

    [Fact]
    public void Fixture_pages_can_be_exported_for_visual_review()
    {
        var output = Environment.GetEnvironmentVariable("CEHO_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        TestEngine.Place(_root); // Tiny local version-echo script, never a real network engine.
        Set("_engineVersion", Installer.EngineVersion);
        Set("_engineVersionAtUtc", DateTime.UtcNow);
        Auth.SetPassword(_cfg, "fixture-panel-only");
        _web.PanelPreflight = _ => Array.Empty<Preflight.Check>(); // Supplied fixture state, no platform probes.
        _cfg.Apps.Add(new AppEntry { Name = "Cursor", Folder = Path.Combine(_root, "Cursor") });
        _cfg.Apps.Add(new AppEntry { Name = "Claude", Folder = Path.Combine(_root, "Claude") });
        foreach (var language in new[] { "en", "ru" })
        {
            _cfg.Language = language;
            _cfg.Subscriptions[0].Name = language == "ru" ? "Пример подписки" : "Example subscription";
            _state = new(true, "NL", "203.0.113.9", null, true);
            _samples = new[]
            {
                new WebServer.AppLive(_cfg.Apps[0].Folder, 3, 4, 0, 4, 0),
                new WebServer.AppLive(_cfg.Apps[1].Folder, 2, 2, 0, 2, 0),
                new WebServer.AppLive(_cfg.Apps[2].Folder, 0, 0, 0),
            };
            Refresh();
            void Save(string name, string html)
            {
                var label = language == "ru" ? "Пример интерфейса · демонстрационные данные · реальный VPN не запускается"
                    : "UI fixture · example data · no real VPN is started";
                html = Regex.Replace(html, "<body([^>]*)>", m => m.Value
                    + "<aside class=fixture-banner style=\"text-align:center;padding:8px;font-size:12px\" role=note>"
                    + WebUtility.HtmlEncode(label) + "</aside>", RegexOptions.CultureInvariant);
                File.WriteAllText(Path.Combine(output, name + "-" + language + ".html"), html);
                if (language == "en") File.WriteAllText(Path.Combine(output, name + ".html"), html);
            }
            Save("state", Render());
            Save("settings", Render()); // The browser opens the real settings disclosure.
            Save("apps", Render("apps"));
            Save("wizard", Render("state", "3"));
            Save("subs", Render("subs"));
            Save("help", Render("help"));
            Save("doctor", Render("doctor"));
            var clock = new FixtureTime();
            var fakeJob = new Job(clock) { Id = "fixture-job", Kind = "power", OperationIdentity = "start", Title = language == "ru" ? "Запуск соединения" : "Starting connection" };
            fakeJob.SetStartupStep(1);
            fakeJob.Report(language == "ru" ? "Подготовка" : "Preparing", null, newPhase: true);
            clock.Advance(TimeSpan.FromSeconds(4));
            fakeJob.SetStartupStep(2);
            fakeJob.Report(language == "ru" ? "Подключение" : "Connecting", null, newPhase: true, waiting: true);
            void SaveJob(string name)
            {
                Save(name, Render(job: fakeJob));
                var snapshot = fakeJob.Snapshot();
                var payload = new
                {
                    snapshot.Id, snapshot.Kind, snapshot.Title,
                    State = snapshot.State switch { JobState.Running => "running", JobState.Done => "done", _ => "failed" },
                    snapshot.Percent, snapshot.Stage, snapshot.Result, snapshot.IsError,
                    Relaunch = snapshot.RelaunchPanel, snapshot.Seconds, snapshot.Indeterminate,
                    snapshot.StageSeconds, snapshot.IdleSeconds, snapshot.IsSlow, snapshot.LastUpdatedUtc,
                    snapshot.CanCancel, snapshot.Phase, snapshot.StageStartedUtc, snapshot.Waiting, snapshot.StartupStep, snapshot.Revision,
                };
                File.WriteAllText(Path.Combine(output, name + "-" + language + ".json"),
                    System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
                    { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            }
            // Register only this in-memory fixture so RenderState sees the same active
            // operation as RenderJob. A startup fixture must not retain a green tunnel
            // from the earlier connected-state example. No operation delegate runs.
            _state = new(false, null, null, null, false);
            var jobsGate = typeof(Jobs).GetField("Gate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var jobs = (Dictionary<string, Job>)typeof(Jobs).GetField("All", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            lock (jobsGate) jobs.Add(fakeJob.Id, fakeJob);
            try
            {
                Assert.Contains("class=\"hero wait\"", Render(job: fakeJob));
                SaveJob("job");
                SaveJob("startup-progress");
                clock.Advance(TimeSpan.FromSeconds(42));
                SaveJob("startup-delayed");
                fakeJob.Complete(language == "ru" ? "Пример: время ожидания запуска истекло" : "Fixture: engine readiness timed out", failed: true);
                Assert.DoesNotContain("class=\"hero on\"", Render(job: fakeJob));
                SaveJob("startup-error");
            }
            finally { lock (jobsGate) jobs.Remove(fakeJob.Id); }
            _state = new(true, "NL", "203.0.113.9", null, true);
            _samples = new[] { new WebServer.AppLive(_cfg.Apps[0].Folder, 1, 1, 2) };
            Refresh();
            Save("leak", Render());
            _state = new(false, null, null, null, false);
            Refresh();
            Save("stopped", Render());
            _samples = _cfg.Apps.Select(app => new WebServer.AppLive(app.Folder, 0, 0, 0)).ToArray();
            Refresh();
            Save("idle", Render());
        }

    }
    private sealed class FixtureTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero).AddTicks(_ticks);
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }

}

using System.Reflection;
using System.Text.Json;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("verified-panel")]
public sealed class AppAddAdmissionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ceho-add-admission-").FullName;
    private string ConfigPath => Path.Combine(_root, "config.json");
    private readonly WebServer _web;
    private bool _running;
    private int _applies, _restarts;
    public AppAddAdmissionTests()
    {
        new CehoConfig { Language = "en" }.Save(ConfigPath);
        _web = new WebServer(ConfigPath, () => new(_running, null, null, null, false), _ => { })
        {
            OnApply = _ => { Interlocked.Increment(ref _applies); return Task.FromResult("applied"); },
            OnRestart = _ => { Interlocked.Increment(ref _restarts); return Task.FromResult<string?>(null); },
        };
    }
    public void Dispose() { Directory.Delete(_root, true); }
    private string App(string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var path = Path.Combine(directory, "program.bin");
        File.WriteAllText(path, "Inert test app fixture; never execute.");
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_add_route_defers_and_duplicate_does_not_save_or_apply(bool running)
    {
        _running = running;
        foreach (var route in new[] { "/apps/add", "/apps/detected", "/apps/installed" })
        {
            var path = App(route.Split('/').Last());
            _web.InstalledApps = _ => new[] { new InstalledAppCatalog.Entry("Installed fixture", path, "test") };
            var accepted = await Post(route, new() { ["path"] = path, ["name"] = "Detected fixture" });
            Assert.False(accepted.IsError);
            Assert.Null(accepted.JobId);
            var before = File.ReadAllBytes(ConfigPath);
            var stamp = File.GetLastWriteTimeUtc(ConfigPath);
            var duplicate = await Post(route, new() { ["path"] = path });
            Assert.False(duplicate.IsError);
            Assert.Contains("already", duplicate.Message!, StringComparison.OrdinalIgnoreCase);
            Assert.Null(duplicate.JobId);
            Assert.Equal(before, File.ReadAllBytes(ConfigPath));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(ConfigPath));
        }
        Assert.Equal(3, CehoConfig.Load(ConfigPath).Apps.Count);
        Assert.Equal(0, _applies);
        Assert.Equal(0, _restarts);
        Assert.True(State().GetProperty("pending").GetBoolean());
    }

    [Fact]
    public async Task Concurrent_tabs_admit_one_identity_without_restarting()
    {
        _running = true;
        var path = App("two-tabs");
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Post("/apps/add", new() { ["path"] = path }))));
        Assert.All(results, r => Assert.False(r.IsError));
        Assert.Single(CehoConfig.Load(ConfigPath).Apps);
        Assert.Single(results.Where(r => r.Message!.Contains("Rule saved", StringComparison.Ordinal)));
        Assert.Equal(0, _applies + _restarts);
    }

    [Fact]
    public async Task Different_executable_in_covered_folder_is_reported_as_coverage_not_duplicate()
    {
        var first = App("shared-directory");
        await Post("/apps/add", new() { ["path"] = first });
        var second = Path.Combine(Path.GetDirectoryName(first)!, "second.bin");
        File.WriteAllText(second, "inert");
        var before = File.ReadAllBytes(ConfigPath);
        var result = await Post("/apps/add", new() { ["path"] = second });
        Assert.False(result.IsError);
        Assert.Contains("covered", result.Message);
        Assert.DoesNotContain("already added", result.Message);
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
        Assert.Single(CehoConfig.Load(ConfigPath).Apps);
    }

    [Fact]
    public async Task Disabled_equivalent_rule_is_preserved_without_creating_ambiguous_shared_folder()
    {
        var path = App("disabled-folder");
        await Post("/apps/add", new() { ["path"] = path });
        var cfg = CehoConfig.Load(ConfigPath);
        cfg.Apps[0].Enabled = false;
        cfg.Save(ConfigPath);
        var second = Path.Combine(Path.GetDirectoryName(path)!, "second.bin");
        File.WriteAllText(second, "inert");
        var before = File.ReadAllBytes(ConfigPath);
        var result = await Post("/apps/add", new() { ["path"] = second });
        Assert.False(result.IsError);
        Assert.Contains("disabled", result.Message);
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
        Assert.False(Assert.Single(CehoConfig.Load(ConfigPath).Apps).Enabled);
        Assert.Equal(0, _applies + _restarts);
    }

    [Fact]
    public async Task Same_basename_in_distinct_directories_can_be_added()
    {
        await Post("/apps/add", new() { ["path"] = App("editor-alpha") });
        await Post("/apps/add", new() { ["path"] = App("editor-beta") });
        var apps = CehoConfig.Load(ConfigPath).Apps;
        Assert.Equal(2, apps.Count);
        Assert.NotEqual(AppIdentity.Id(apps[0]), AppIdentity.Id(apps[1]));
    }

    [Fact]
    public async Task Linux_case_distinct_paths_do_not_collide_or_remove_each_other()
    {
        if (!Os.IsLinux) return;
        var first = App("Editor");
        var second = App("editor");
        await Post("/apps/add", new() { ["path"] = first });
        await Post("/apps/add", new() { ["path"] = second });
        var apps = CehoConfig.Load(ConfigPath).Apps;
        Assert.Equal(2, apps.Count);
        await Post("/apps/rename", new() { ["folder"] = apps[1].Folder, ["displayName"] = "lowercase only" });
        var renamed = CehoConfig.Load(ConfigPath).Apps;
        Assert.Null(renamed[0].DisplayName);
        Assert.Equal("lowercase only", renamed[1].DisplayName);
    }

    [Fact]
    public async Task New_tunnel_drop_requires_confirmation_and_does_not_execute_selected_file()
    {
        var path = App("drop");
        var before = File.ReadAllBytes(ConfigPath);
        var missing = await Post("/apps/add", new() { ["path"] = path, ["intent"] = "tunnel" });
        Assert.True(missing.IsError);
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
        var accepted = await Post("/apps/add", new() { ["path"] = path, ["intent"] = "tunnel", ["confirm_add"] = "1" });
        Assert.False(accepted.IsError);
        Assert.Single(CehoConfig.Load(ConfigPath).Apps);
        Assert.Equal(0, _applies + _restarts);
    }

    [Fact]
    public async Task Active_apply_requires_acknowledgment_and_failed_apply_keeps_pending()
    {
        _running = true;
        await Post("/apps/add", new() { ["path"] = App("pending") });
        var rejected = await Post("/apply", new());
        Assert.True(rejected.IsError);
        Assert.Null(rejected.JobId);
        Assert.Equal(0, _restarts);
        _web.OnRestart = _ => Task.FromResult<string?>("Injected failure");
        var failed = await Post("/apply", new() { ["confirm_apply"] = "1" });
        await Finished(failed.JobId!);
        Assert.Equal(JobState.Failed, Jobs.Find(failed.JobId)!.State);
        Assert.True(State().GetProperty("pending").GetBoolean());
        Assert.False(_web.IsAppRuleApplied(CehoConfig.Load(ConfigPath).Apps[0]));
        _web.OnRestart = _ => { Interlocked.Increment(ref _restarts); return Task.FromResult<string?>(null); };
        var applied = await Post("/apply", new() { ["confirm_apply"] = "1" });
        await Finished(applied.JobId!);
        Assert.Equal(1, _restarts);
        Assert.False(State().GetProperty("pending").GetBoolean());
        Assert.True(_web.IsAppRuleApplied(CehoConfig.Load(ConfigPath).Apps[0]));
    }

    [Fact]
    public async Task Applied_state_is_not_inferred_from_running_and_is_invalidated_by_new_edit()
    {
        _running = true;
        var first = App("first");
        await Post("/apps/add", new() { ["path"] = first });
        Assert.False(_web.IsAppRuleApplied(CehoConfig.Load(ConfigPath).Apps[0]));
        var applied = await Post("/apply", new() { ["confirm_apply"] = "1" });
        await Finished(applied.JobId!);
        Assert.True(_web.IsAppRuleApplied(CehoConfig.Load(ConfigPath).Apps[0]));
        await Post("/apps/add", new() { ["path"] = App("later") });
        Assert.All(CehoConfig.Load(ConfigPath).Apps, app => Assert.False(_web.IsAppRuleApplied(app)));
    }

    [Fact]
    public async Task Start_completed_in_engine_queue_cannot_restart_without_apply_acknowledgment()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _web.OnApplyWithRestartConfirmation = async (allowed, report) =>
        {
            entered.TrySetResult();
            await release.Task;
            return await TunnelRuleApply.RunAsync(report, () => new NoopGate(), () => _running,
                _ => { Interlocked.Increment(ref _restarts); return Task.FromResult<string?>(null); },
                _ => { Interlocked.Increment(ref _applies); return Task.FromResult("applied"); }, "restarted", allowed);
        };
        await Post("/apps/add", new() { ["path"] = App("race") });
        var apply = await Post("/apply", new());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _running = true;
        }
        finally { release.TrySetResult(); }
        await Finished(apply.JobId!);
        Assert.Equal(JobState.Failed, Jobs.Find(apply.JobId)!.State);
        Assert.Equal(0, _applies + _restarts);
        Assert.True(State().GetProperty("pending").GetBoolean());
    }

    [Fact]
    public async Task Successful_apply_ignores_subscription_status_and_autostart_metadata_writes()
    {
        _running = true;
        await Post("/apps/add", new() { ["path"] = App("metadata") });
        var config = CehoConfig.Load(ConfigPath);
        config.Subscriptions.Add(new SubscriptionEntry { Name = "fixture", Url = "https://example.invalid/sub" });
        config.Save(ConfigPath);
        _web.OnRestart = _ =>
        {
            var fresh = CehoConfig.Load(ConfigPath);
            fresh.AutostartOffered = true;
            fresh.Subscriptions[0].LastCheckedUtc = DateTime.UtcNow.ToString("O");
            fresh.Subscriptions[0].LastCheckOk = true;
            fresh.Subscriptions[0].LastNodes = 3;
            fresh.SaveSubscriptionStatus(ConfigPath);
            return Task.FromResult<string?>(null);
        };
        var result = await Post("/apply", new() { ["confirm_apply"] = "1" });
        await Finished(result.JobId!);
        Assert.False(State().GetProperty("pending").GetBoolean());
        Assert.True(_web.IsAppRuleApplied(CehoConfig.Load(ConfigPath).Apps[0]));
    }

    [Fact]
    public async Task Noop_start_of_running_tunnel_does_not_bless_pending_rules()
    {
        _running = true;
        await Post("/apps/add", new() { ["path"] = App("stale-start") });
        _web.OnStart = _ => Task.FromResult<string?>(null);
        var result = await Post("/control/start", new());
        await Finished(result.JobId!);
        Assert.True(State().GetProperty("pending").GetBoolean());
        Assert.False(_web.IsAppRuleApplied(CehoConfig.Load(ConfigPath).Apps[0]));
    }

    [Fact]
    public async Task Actual_start_receipt_only_confirms_the_engine_configuration()
    {
        await Post("/apps/add", new() { ["path"] = App("receipt") });
        var generated = CehoConfig.Load(ConfigPath);
        _running = true;
        var changed = CehoConfig.Load(ConfigPath);
        changed.MixedPort++;
        changed.Save(ConfigPath);
        _web.NotifyRulesApplied(generated);
        Assert.True(State().GetProperty("pending").GetBoolean());
        Assert.False(_web.IsAppRuleApplied(changed.Apps[0]));
        _web.NotifyRulesApplied(changed);
        Assert.False(State().GetProperty("pending").GetBoolean());
        Assert.True(_web.IsAppRuleApplied(changed.Apps[0]));
        _web.NotifyEngineStateChanged();
        Assert.False(_web.IsAppRuleApplied(changed.Apps[0]));
    }

    [Fact]
    public async Task Stop_is_admitted_promptly_during_slow_apply_and_new_routes_are_not_blessed()
    {
        _running = true;
        await Post("/apps/add", new() { ["path"] = App("slow-apply") });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _web.OnApplyWithRestartConfirmation = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return "applied old rules";
        };
        _web.OnStop = () => { _running = false; return Task.FromResult<string?>(null); };
        var applying = await Post("/apply", new() { ["confirm_apply"] = "1" });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopped = await Post("/control/stop", new()).WaitAsync(TimeSpan.FromSeconds(1));
            await Finished(stopped.JobId!);
            var changed = CehoConfig.Load(ConfigPath);
            changed.FailClosed = !changed.FailClosed;
            changed.Save(ConfigPath);
        }
        finally { release.TrySetResult(); }
        await Finished(applying.JobId!);
        Assert.True(State().GetProperty("pending").GetBoolean());
        Assert.All(CehoConfig.Load(ConfigPath).Apps, app => Assert.False(_web.IsAppRuleApplied(app)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Last_app_removal_reconciles_admitted_guard_but_ordinary_stop_does_not(bool running)
    {
        _running = running;
        await Post("/apps/add", new() { ["path"] = App("remove-guard") });
        var config = CehoConfig.Load(ConfigPath);
        var scope = new AdmittedConfiguration(config);
        var removeCalls = 0;
        _web.OnStop = () => { _running = false; return Task.FromResult<string?>(null); };
        _web.OnRemoveLastApp = removed =>
        {
            removeCalls++;
            scope.Admit(removed);
            _running = false;
            return Task.FromResult<string?>(null);
        };
        var stop = await Post("/control/stop", new());
        await Finished(stop.JobId!);
        Assert.Single(scope.Snapshot().Apps);
        Assert.Equal(0, removeCalls);
        var remove = await Post("/apps/remove", new() { ["folder"] = config.Apps[0].Folder });
        await Finished(remove.JobId!);
        Assert.Equal(1, removeCalls);
        Assert.Empty(scope.Snapshot().Apps);
        Assert.Empty(CehoConfig.Load(ConfigPath).Apps);
    }

    [Fact]
    public async Task Last_app_removal_is_rejected_before_save_while_start_is_active()
    {
        await Post("/apps/add", new() { ["path"] = App("removal-race") });
        var config = CehoConfig.Load(ConfigPath);
        var before = File.ReadAllBytes(ConfigPath);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var removals = 0;
        _web.OnStart = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            _running = true;
            return null;
        };
        _web.OnRemoveLastApp = removed =>
        {
            Assert.Empty(removed.Apps);
            removals++;
            _running = false;
            return Task.FromResult<string?>(null);
        };
        var starting = await Post("/control/start", new());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var blocked = await Post("/apps/remove", new() { ["folder"] = config.Apps[0].Folder });
            Assert.True(blocked.IsError);
            Assert.Equal(starting.JobId, blocked.JobId);
            Assert.Equal(before, File.ReadAllBytes(ConfigPath));
            Assert.Single(CehoConfig.Load(ConfigPath).Apps);
            Assert.Equal(0, removals);
        }
        finally { release.TrySetResult(); }
        await Finished(starting.JobId!);
        var retried = await Post("/apps/remove", new() { ["folder"] = config.Apps[0].Folder });
        await Finished(retried.JobId!);
        Assert.Equal(1, removals);
        Assert.Empty(CehoConfig.Load(ConfigPath).Apps);
    }

    private JsonElement State() => JsonSerializer.SerializeToElement(_web.TunnelState(CehoConfig.Load(ConfigPath)));
    private Task<(string? Message, bool IsError, string? JobId)> Post(string route, Dictionary<string, string> form)
    {
        var method = typeof(WebServer).GetMethod("ApplyPostAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task<(string? Message, bool IsError, string? JobId)>)method.Invoke(_web, new object[] { route, form, CehoConfig.Load(ConfigPath) })!;
    }
    private static async Task Finished(string id)
    {
        var job = Jobs.Find(id)!;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (job.Running && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.False(job.Running);
    }
    private sealed class NoopGate : IDisposable { public void Dispose() { } }
}

using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class AdmittedConfigurationTests
{
    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.Mac)]
    public void Guard_and_automatic_recovery_keep_admitted_scope_until_explicit_apply(OsKind platform)
    {
        var oldPath = platform == OsKind.Windows ? @"C:\Apps\old" : "/opt/old";
        var pendingPath = platform == OsKind.Windows ? @"C:\Apps\pending" : "/opt/pending";
        var admitted = new CehoConfig { MixedPort = 2080, FailClosed = true };
        admitted.Apps.Add(new AppEntry { Name = "old", Folder = oldPath });
        var scope = new AdmittedConfiguration(admitted);
        var saved = scope.Snapshot();
        saved.Apps.Add(new AppEntry { Name = "pending", Folder = pendingPath });
        saved.MixedPort = 2088;
        saved.FailClosed = false;
        var diskReads = 0;
        CehoConfig ReadSaved() { diskReads++; return saved; }

        // Each platform's watchdog and off-tunnel fail-closed recovery sees only admitted apps.
        Assert.Single(scope.Snapshot().Apps);
        var retry = scope.ForStart(preserveAdmitted: true, ReadSaved);
        Assert.Single(retry.Apps);
        Assert.Equal(2080, retry.MixedPort);
        Assert.True(retry.FailClosed);
        Assert.Equal(0, diskReads);

        // Explicit Apply/Start may admit the user's saved changes.
        var explicitStart = scope.ForStart(preserveAdmitted: false, ReadSaved);
        Assert.Equal(1, diskReads);
        Assert.Equal(2, explicitStart.Apps.Count);
        Assert.Single(scope.Snapshot().Apps); // reading saved settings alone has no network effect
        scope.Admit(explicitStart);
        Assert.Equal(2, scope.Snapshot().Apps.Count);
        Assert.Equal(2088, scope.Snapshot().MixedPort);
        Assert.False(scope.Snapshot().FailClosed);
    }

    [Fact]
    public void Snapshots_are_deep_copies_and_retry_port_changes_cannot_overwrite_pending_settings()
    {
        var original = new CehoConfig { Apps = new() { new AppEntry { Name = "old", Folder = "/old" } } };
        var scope = new AdmittedConfiguration(original);
        original.Apps[0].AllowedNodes.Add("a newer saved selection");
        original.Apps.Add(new AppEntry { Name = "pending", Folder = "/pending" });
        Assert.Single(scope.Snapshot().Apps);
        Assert.Empty(scope.Snapshot().Apps[0].AllowedNodes);
        var retry = scope.Snapshot();
        retry.MixedPort++;
        retry.Apps.Clear();
        Assert.Single(scope.Snapshot().Apps);
        Assert.Equal(2080, scope.Snapshot().MixedPort);
        Assert.Equal(2, original.Apps.Count);
    }

    [Fact]
    public void Production_watchdogs_and_retries_use_admitted_configuration_not_pending_disk_rules()
    {
        var source = ReadSource("src/ProxyCage.Cli/Program.cs");
        var guard = Between(source, "    async Task StartGuard()", "    void ScheduleGuardRecovery(");
        Assert.Contains("var c = admittedConfiguration.Snapshot();", guard);
        Assert.DoesNotContain("CehoConfig.Load", guard);
        var startup = Between(source, "    async Task<string?> StartTunnelLocked(", "    void ScheduleRecoveryLocked(");
        Assert.Contains("var keepAdmitted = automatic || preserveAdmitted;", startup);
        Assert.Single(startup.Split("admittedConfiguration.ForStart(keepAdmitted,").Skip(1));
        Assert.Contains("c = admittedConfiguration.Snapshot();", startup);
        Assert.Contains("if (!keepAdmitted) c.Save(Ceho.ConfigPath);", startup);
        Assert.Contains("admittedConfiguration.Admit(c);", startup);
        var observer = Between(source, "                var observedGeneration = recovery.Generation;", "            try { await Task.Delay(TimeSpan.FromSeconds(30)");
        Assert.Contains("LeakGuard.Apply(admittedConfiguration.Snapshot(), Ceho.Root);", observer);
        var guardApply = observer.IndexOf("LeakGuard.Apply", StringComparison.Ordinal);
        Assert.Contains("using var gate = EngineMutex.Acquire(Ceho.Root);", observer[..guardApply]);
        Assert.DoesNotContain("CehoConfig.Load(Ceho.ConfigPath)", observer);
        Assert.Contains("var current = activeEngineConfig ?? admittedConfiguration.Snapshot();", observer);
        Assert.Contains("StartTunnelLocked(report, \"engine-update\", S(\"reconnect_engine_update\"), preserveAdmitted: true)", source);
        Assert.Contains("StartTunnelLocked(report, \"engine-rollback\", S(\"reconnect_engine_rollback\"), preserveAdmitted: true)", source);
    }

    [Fact]
    public void Off_tunnel_apply_uses_one_exact_admitted_configuration()
    {
        var program = ReadSource("src/ProxyCage.Cli/Program.cs");
        var apply = Between(program, "    web.OnApply =", "    web.WrappedNames");
        Assert.Contains("admittedConfiguration.Admit(admitted);", apply);
        Assert.Contains("Ceho.ApplyConfigurationAsync(admitted, p)", apply);
        Assert.Contains("if (guard is not null) { StopGuard(); await StartGuard(); }", apply);
        var ceho = ReadSource("src/ProxyCage.Cli/Ceho.cs");
        var configured = Between(ceho, "    internal static async Task<string> ApplyConfigurationAsync(", "    public static Task<(string? Country, string? Ip)> ProbeExitAsync(");
        Assert.DoesNotContain("CehoConfig.Load", configured);
        Assert.Contains("LeakGuard.Apply(cfg, Root)", configured);
        Assert.Contains("SingBoxConfigGenerator.GenerateForConfig(nodes, cfg)", configured);
    }

    [Fact]
    public void Only_last_app_removal_replaces_guard_scope_while_ordinary_stop_preserves_it()
    {
        var source = ReadSource("src/ProxyCage.Cli/Program.cs");
        var ordinaryStop = Between(source, "    web.OnStop =", "    web.OnRemoveLastApp =");
        Assert.DoesNotContain("admittedConfiguration.Admit", ordinaryStop);
        var lastApp = Between(source, "    web.OnRemoveLastApp =", "    web.OnRestart =");
        Assert.Contains("admittedConfiguration.Admit(removed);", lastApp);
        Assert.Contains("StopGuard();", lastApp);
        Assert.Contains("LeakGuard.Apply(removed, Ceho.Root)", lastApp);
    }

    private static string Between(string text, string first, string last)
    {
        var start = text.IndexOf(first, StringComparison.Ordinal);
        Assert.True(start >= 0, first);
        var end = text.IndexOf(last, start + first.Length, StringComparison.Ordinal);
        Assert.True(end > start, last);
        return text[start..end];
    }
    private static string ReadSource(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProxyCage.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}

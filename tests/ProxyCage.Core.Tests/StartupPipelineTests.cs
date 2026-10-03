namespace ProxyCage.Core.Tests;

/// <summary>
/// Architectural regressions are checked from source instead of running the daemon:
/// these paths control real firewall, routes and processes and are unsafe in unit tests.
/// Runtime readiness and queue behavior are covered separately with injected delegates.
/// </summary>
public class StartupPipelineTests
{
    private static string ProgramSource() => ReadSource("src/ProxyCage.Cli/Program.cs");

    private static string ReadSource(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }

    private static string Between(string source, string begin, string end)
    {
        var first = source.IndexOf(begin, StringComparison.Ordinal);
        Assert.True(first >= 0, begin);
        var last = source.IndexOf(end, first + begin.Length, StringComparison.Ordinal);
        Assert.True(last > first, end);
        return source[first..last];
    }

    [Fact]
    public void Restart_holds_one_queue_entry_across_stop_and_start_and_rechecks_user_intent()
    {
        var restart = Between(ProgramSource(), "    async Task<string?> RestartTunnel(", "    web.OnStart =");
        var gate = restart.IndexOf("using var gate = EngineMutex.Acquire", StringComparison.Ordinal);
        var stop = restart.IndexOf("StopTunnelLocked();", StringComparison.Ordinal);
        var start = restart.IndexOf("await StartTunnelLocked(", StringComparison.Ordinal);
        Assert.True(gate >= 0 && stop > gate && start > stop);
        Assert.Contains("onlyIfWanted && !wanted", restart);
        Assert.DoesNotContain("StopTunnel();", restart);
        Assert.DoesNotContain("await StartTunnel(report)", restart);
    }

    [Fact]
    public void Stop_changes_desired_state_only_inside_the_engine_queue()
    {
        var stop = Between(ProgramSource(), "    web.OnStop =", "    web.OnRestart =");
        var gate = stop.IndexOf("using var gate = EngineMutex.Acquire", StringComparison.Ordinal);
        var intent = stop.IndexOf("wanted = false;", StringComparison.Ordinal);
        Assert.True(gate >= 0 && intent > gate);
        Assert.Contains("recovery.StopByUser()", stop);
        Assert.Contains("StopTunnelLocked()", stop);
    }

    [Fact]
    public void Checkpoint_restore_is_locked_and_does_not_regenerate_the_verified_rules()
    {
        var restore = Between(ProgramSource(), "    web.OnRestoreVerified =", "    web.OnApply =");
        var gate = restore.IndexOf("using var gate = EngineMutex.Acquire", StringComparison.Ordinal);
        var apply = restore.IndexOf("VerifiedConfigStore.Restore(", StringComparison.Ordinal);
        Assert.True(gate >= 0 && apply > gate);
        Assert.Contains("VerifiedConfigStore.Restore(Ceho.Root, acknowledgeSecurity, expectedVerifiedUtc)", restore);
        Assert.Contains("BringEngineUp(restored, connection)", restore);
        Assert.DoesNotContain("StartTunnelLocked(", restore);
        Assert.DoesNotContain("Ceho.ApplyAsync", restore);
        Assert.DoesNotContain("CommitVerified", restore);
    }

    [Fact]
    public void Startup_reports_unknown_work_as_phases_and_requires_real_readiness()
    {
        var program = ProgramSource();
        var startup = Between(program, "    async Task<string?> StartTunnel(", "    string? StopTunnel()");
        Assert.DoesNotContain("report?.Stage(", startup);
        Assert.Contains("report?.Phase(Strings.T(cfg.Language, \"stage_engine_queue\"), waiting: true)", startup);
        Assert.Contains("readiness == EngineReadinessResult.Ready && p.IsRunning", startup);
        Assert.Contains("engine_ready_timeout", startup);
        Assert.Contains("await StartGuard();", startup);
        var load = Between(ReadSource("src/ProxyCage.Cli/Ceho.cs"),
            "    public static async Task<IReadOnlyList<ProxyNode>> LoadAllNodesAsync(",
            "    public static async Task<IReadOnlyList<NodeProbe.CountryRow>> RefreshNodeCountriesAsync(");
        Assert.Contains("report?.Note", load);
        Assert.DoesNotContain("95 + current / total", load);
    }

    [Fact]
    public void Panel_mutations_reload_configuration_under_one_admission_gate()
    {
        var source = ReadSource("src/ProxyCage.Core/WebServer.cs");
        var admission = Between(source, "    private async Task<(string? Message, bool IsError, string? JobId)> ApplyPostAsync(",
            "    private async Task<(string? Message, bool IsError, string? JobId)> ApplyPostCoreAsync(");
        var gate = admission.IndexOf("await _configGate.WaitAsync()", StringComparison.Ordinal);
        var reload = admission.IndexOf("CehoConfig.Load(_configPath)", StringComparison.Ordinal);
        Assert.True(gate >= 0 && reload > gate);
        Assert.Contains("finally { _configGate.Release(); }", admission);
        Assert.Contains("Jobs.Start(JobRestore", source);
        Assert.Contains("finally { InvalidatePanelData(); }", source);
    }
    [Fact]
    public void Every_engine_start_checks_preflight_before_downloading_subscriptions()
    {
        var start = Between(ProgramSource(), "    async Task<string?> StartTunnelLocked(", "    void ScheduleRecoveryLocked(");
        var validate = start.IndexOf("c.Validate();", StringComparison.Ordinal);
        var cheap = start.IndexOf("StartupPreflight.ConfigurationError(c)", StringComparison.Ordinal);
        var rights = start.IndexOf("Preflight.Run(c, Ceho.Root)", StringComparison.Ordinal);
        var api = start.IndexOf("Preflight.TcpPortTaken(c.ClashApiPort)", StringComparison.Ordinal);
        var fetch = start.IndexOf("await Ceho.LoadAllNodesAsync", StringComparison.Ordinal);
        Assert.True(validate >= 0 && cheap > validate && rights > cheap && api > rights && fetch > api);
        Assert.Contains("PrivateFile.Write(Ceho.RuntimeConfigPath, generatedRules)", start);
    }

    [Fact]
    public void Automatic_recovery_uses_the_budget_and_rejects_stale_probe_generations()
    {
        var program = ProgramSource();
        var automatic = Between(program, "    async Task QueueAutomaticRecovery(", "    async Task<string?> BringEngineUp(");
        Assert.Contains("!wanted || !recovery.IsCurrent(generation)", automatic);
        Assert.Contains("ScheduleRecoveryLocked(reasonCode, reason)", automatic);
        Assert.DoesNotContain("StartTunnelLocked", automatic);
        Assert.Contains("recovery.TryBegin(Environment.TickCount64, out var retry)", program);
        Assert.Contains("retry.Reason, automatic: true", program);
        Assert.Contains("runtimeChanged = web.NotifyEngineStateChanged", program);
        Assert.DoesNotContain("RefreshIfDeadAsync", program);
        Assert.DoesNotContain("RefreshIfDeadAsync", ReadSource("src/ProxyCage.Cli/Ceho.cs"));
        Assert.Contains("preferCache: reasonCode != \"exit-unavailable\"", program);
        Assert.Contains("failedExitChecks >= 2 && current.RotationEnabled", program);
    }

    [Fact]
    public void Controller_is_leased_before_any_daemon_startup_mutations()
    {
        var daemon = Between(ProgramSource(), "if (cmd is \"daemon\" or \"web\")", "    SingBoxProcess? proc = null;");
        var lease = daemon.IndexOf("ControllerLease.TryAcquire(Ceho.Root)", StringComparison.Ordinal);
        var stopListener = daemon.IndexOf("DaemonControl.ListenForStop()", StringComparison.Ordinal);
        Assert.True(lease >= 0 && stopListener > lease);
        Assert.Contains("Auth.ReadPanelPointer", daemon);
    }
}

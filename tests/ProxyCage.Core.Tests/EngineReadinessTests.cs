using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class EngineReadinessTests
{
    [Fact]
    public async Task Living_process_without_required_listeners_times_out()
    {
        var result = await EngineReadiness.WaitAsync(() => true, () => Task.FromResult(false),
            timeout: TimeSpan.FromMilliseconds(60), poll: TimeSpan.FromMilliseconds(5));
        Assert.Equal(EngineReadinessResult.TimedOut, result);
    }

    [Fact]
    public async Task Dead_process_is_never_reported_ready()
    {
        var checks = 0;
        var result = await EngineReadiness.WaitAsync(() => false, () => { checks++; return Task.FromResult(true); });
        Assert.Equal(EngineReadinessResult.ProcessExited, result);
        Assert.Equal(0, checks);
    }

    [Fact]
    public async Task Child_that_dies_during_the_probe_is_not_ready()
    {
        var running = true;
        var result = await EngineReadiness.WaitAsync(() => running,
            () => { running = false; return Task.FromResult(true); }, settle: TimeSpan.Zero);
        Assert.Equal(EngineReadinessResult.ProcessExited, result);
    }

    [Fact]
    public async Task Readiness_must_stay_true_for_the_settling_interval()
    {
        var checks = 0;
        var result = await EngineReadiness.WaitAsync(() => true,
            () => Task.FromResult(++checks != 2), timeout: TimeSpan.FromSeconds(3),
            settle: TimeSpan.FromMilliseconds(30), poll: TimeSpan.FromMilliseconds(5));
        Assert.Equal(EngineReadinessResult.Ready, result);
        Assert.True(checks >= 4);
    }

    [Fact]
    public async Task Stalled_probe_is_bounded_by_the_startup_deadline()
    {
        var stalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await EngineReadiness.WaitAsync(() => true, () => stalled.Task,
            timeout: TimeSpan.FromMilliseconds(30));
        Assert.Equal(EngineReadinessResult.TimedOut, result);
    }
}

using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class EngineReadinessTests
{
    [Fact]
    public async Task Living_process_without_required_listeners_times_out()
    {
        var clock = new ManualTimerTimeProvider();
        var checks = 0;
        var operation = EngineReadiness.WaitAsync(() => true,
            () => { checks++; return Task.FromResult(false); },
            timeout: TimeSpan.FromMilliseconds(60), poll: TimeSpan.FromMilliseconds(5),
            timeProvider: clock);

        Assert.Equal(EngineReadinessResult.TimedOut, await clock.RunToCompletionAsync(operation));
        Assert.Equal(TimeSpan.FromMilliseconds(60), clock.Elapsed);
        Assert.Equal(12, checks); // Probes at 0..55 ms; none at or beyond the deadline.
    }

    [Fact]
    public async Task Dead_process_is_never_reported_ready()
    {
        var clock = new ManualTimerTimeProvider();
        var checks = 0;
        var operation = EngineReadiness.WaitAsync(() => false,
            () => { checks++; return Task.FromResult(true); }, timeProvider: clock);

        Assert.Equal(EngineReadinessResult.ProcessExited, await clock.RunToCompletionAsync(operation));
        Assert.Equal(0, checks);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public async Task Child_that_dies_during_the_probe_is_not_ready()
    {
        var clock = new ManualTimerTimeProvider();
        var running = true;
        var checks = 0;
        var operation = EngineReadiness.WaitAsync(() => running,
            () => { checks++; running = false; return Task.FromResult(true); },
            settle: TimeSpan.Zero, timeProvider: clock);

        Assert.Equal(EngineReadinessResult.ProcessExited, await clock.RunToCompletionAsync(operation));
        Assert.Equal(1, checks);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public async Task Readiness_must_stay_true_for_the_settling_interval()
    {
        var clock = new ManualTimerTimeProvider();
        var probeTimes = new List<TimeSpan>();
        var operation = EngineReadiness.WaitAsync(() => true, () =>
        {
            probeTimes.Add(clock.Elapsed);
            return Task.FromResult(probeTimes.Count != 2);
        }, timeout: TimeSpan.FromSeconds(3), settle: TimeSpan.FromMilliseconds(30),
            poll: TimeSpan.FromMilliseconds(5), timeProvider: clock);

        Assert.Equal(EngineReadinessResult.Ready, await clock.RunToCompletionAsync(operation));
        // Ready at 0 ms, false at 5 ms, then continuously ready from 10 to 40 ms.
        // Reusing the first ready timestamp would incorrectly succeed at 30 ms.
        Assert.Equal(TimeSpan.FromMilliseconds(40), clock.Elapsed);
        Assert.Equal(Enumerable.Range(0, 9).Select(i => TimeSpan.FromMilliseconds(i * 5)), probeTimes);
    }

    [Fact]
    public async Task Child_that_dies_while_listeners_are_settling_is_not_ready()
    {
        var clock = new ManualTimerTimeProvider();
        var checks = 0;
        var operation = EngineReadiness.WaitAsync(
            () => clock.Elapsed < TimeSpan.FromMilliseconds(10),
            () => { checks++; return Task.FromResult(true); },
            timeout: TimeSpan.FromSeconds(3), settle: TimeSpan.FromMilliseconds(30),
            poll: TimeSpan.FromMilliseconds(5), timeProvider: clock);

        Assert.Equal(EngineReadinessResult.ProcessExited, await clock.RunToCompletionAsync(operation));
        Assert.Equal(TimeSpan.FromMilliseconds(10), clock.Elapsed);
        Assert.Equal(2, checks); // No listener probe after process death.
    }

    [Fact]
    public async Task Stalled_probe_is_bounded_by_the_startup_deadline()
    {
        var clock = new ManualTimerTimeProvider();
        var stalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;
        var operation = EngineReadiness.WaitAsync(() => true,
            () => { checks++; return stalled.Task; },
            timeout: TimeSpan.FromMilliseconds(30), timeProvider: clock);

        Assert.Equal(EngineReadinessResult.TimedOut, await clock.RunToCompletionAsync(operation));
        Assert.Equal(TimeSpan.FromMilliseconds(30), clock.Elapsed);
        Assert.Equal(1, checks);
        Assert.False(stalled.Task.IsCompleted);
        // A late successful probe must not convert the recorded timeout to readiness.
        stalled.SetResult(true);
        Assert.Equal(EngineReadinessResult.TimedOut, await operation);
    }
}

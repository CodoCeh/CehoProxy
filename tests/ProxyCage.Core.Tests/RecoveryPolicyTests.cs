namespace ProxyCage.Core.Tests;

public class RecoveryPolicyTests
{
    [Fact]
    public void Failures_have_five_increasing_delays_then_a_terminal_result()
    {
        var policy = new RecoveryPolicy();
        policy.StartByUser();
        long now = 1000;
        foreach (var delay in new[] { 5000, 15000, 30000, 60000, 120000 })
        {
            Assert.True(policy.Schedule(policy.Generation, "engine-exited", "reason", now));
            Assert.Equal(now + delay, policy.Pending!.DueAt);
            Assert.False(policy.TryBegin(now + delay - 1, out _));
            Assert.True(policy.TryBegin(now += delay, out var attempt));
            Assert.Equal(policy.Generation, attempt!.Generation);
        }
        Assert.Equal(5, policy.Attempts);
        Assert.False(policy.Schedule(policy.Generation, "engine-exited", "last reason", now));
        Assert.True(policy.Exhausted);
        Assert.Null(policy.Pending);
        Assert.False(policy.TryBegin(long.MaxValue, out _));
        Assert.Contains("last reason", RecoveryPolicy.TerminalMessage("en", policy.Attempts, "last reason"));
        Assert.Contains("Start", RecoveryPolicy.TerminalMessage("en", policy.Attempts, "last reason"));
    }

    [Fact]
    public void Explicit_retry_resets_exhausted_budget_and_invalidates_old_work()
    {
        var policy = new RecoveryPolicy(new[] { TimeSpan.FromMilliseconds(1) });
        policy.StartByUser();
        policy.Schedule(policy.Generation, "failure", "one", 0);
        policy.TryBegin(1, out _);
        policy.Schedule(policy.Generation, "failure", "two", 1);
        var old = policy.Generation;
        Assert.True(policy.Exhausted);
        policy.StartByUser();
        Assert.False(policy.Exhausted);
        Assert.Equal(0, policy.Attempts);
        Assert.False(policy.Schedule(old, "network-changed", "old probe", 5));
        Assert.True(policy.Schedule(policy.Generation, "failure", "new failure", 5));
    }

    [Fact]
    public void Stop_cancels_pending_work_even_after_an_arbitrary_sleep()
    {
        var policy = new RecoveryPolicy();
        policy.StartByUser();
        var old = policy.Generation;
        policy.Schedule(old, "network-changed", "wifi", 0);
        policy.StopByUser();
        Assert.False(policy.Wanted);
        Assert.False(policy.TryBegin(long.MaxValue, out _));
        Assert.False(policy.Schedule(old, "network-changed", "late probe", 500000));
        Assert.Null(policy.Pending);
    }

    [Fact]
    public void Successful_launch_does_not_make_a_crash_loop_unbounded()
    {
        var policy = new RecoveryPolicy(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) });
        policy.StartByUser();
        policy.Schedule(policy.Generation, "crash", "one", 0);
        policy.TryBegin(1000, out _);
        policy.Started(1100);
        policy.Schedule(policy.Generation, "crash", "two", 1200);
        Assert.Equal(3200, policy.Pending!.DueAt);
        policy.TryBegin(3200, out _);
        policy.Started(3300);
        Assert.False(policy.Schedule(policy.Generation, "crash", "three", 3400));
        Assert.True(policy.Exhausted);
    }

    [Fact]
    public void A_five_minute_stable_session_renews_the_budget()
    {
        var policy = new RecoveryPolicy(new[] { TimeSpan.FromSeconds(1) });
        policy.StartByUser();
        policy.Schedule(policy.Generation, "crash", "one", 0);
        policy.TryBegin(1000, out _);
        policy.Started(1100);
        Assert.True(policy.Schedule(policy.Generation, "network-changed", "new session", 301100));
        Assert.Equal(0, policy.Attempts);
        Assert.False(policy.Exhausted);
    }

    [Fact]
    public void Duplicate_fault_observations_do_not_extend_the_due_time()
    {
        var policy = new RecoveryPolicy();
        policy.StartByUser();
        var generation = policy.Generation;
        policy.Schedule(generation, "crash", "first", 0);
        Assert.False(policy.Schedule(generation, "network-changed", "second", 4000));
        Assert.Equal("first", policy.Pending!.Reason);
        Assert.True(policy.TryBegin(5000, out _));
        Assert.False(policy.IsCurrent(generation));
    }

    [Fact]
    public void A_resume_after_deadline_admits_only_one_attempt()
    {
        var policy = new RecoveryPolicy();
        policy.StartByUser();
        policy.Schedule(policy.Generation, "crash", "one", 0);
        Assert.True(policy.TryBegin(3600000, out _));
        Assert.False(policy.TryBegin(3600000, out _));
        Assert.Equal(1, policy.Attempts);
    }

    [Fact]
    public void Invalid_or_decreasing_delay_schedules_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryPolicy(Array.Empty<TimeSpan>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryPolicy(new[] { TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryPolicy(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1) }));
    }
    [Fact]
    public void Engine_readiness_alone_does_not_reset_the_network_failure_budget()
    {
        var policy = new RecoveryPolicy(new[] { TimeSpan.FromSeconds(1) });
        policy.StartByUser();
        policy.Schedule(policy.Generation, "exit-unavailable", "offline", 0);
        policy.TryBegin(1000, out _);
        policy.Started(1100, healthy: false);
        Assert.False(policy.Schedule(policy.Generation, "exit-unavailable", "still offline", 9999999));
        Assert.True(policy.Exhausted);
    }

    [Fact]
    public void A_failed_connectivity_check_breaks_the_stable_period()
    {
        var policy = new RecoveryPolicy(new[] { TimeSpan.FromSeconds(1) });
        policy.StartByUser();
        policy.Schedule(policy.Generation, "crash", "offline", 0);
        policy.TryBegin(1000, out _);
        policy.Started(1100, healthy: false);
        policy.ObserveHealthy(1200);
        policy.ObserveUnhealthy();
        policy.ObserveHealthy(301100);
        Assert.False(policy.Schedule(policy.Generation, "crash", "again", 301200));
        Assert.True(policy.Exhausted);
    }
    [Fact]
    public void Verified_stable_connectivity_renews_budget_before_a_later_outage()
    {
        var policy = new RecoveryPolicy(new[] { TimeSpan.FromSeconds(1) });
        policy.StartByUser();
        policy.Schedule(policy.Generation, "exit-unavailable", "offline", 0);
        policy.TryBegin(1000, out _);
        policy.Started(1100, healthy: false);
        policy.ObserveHealthy(1200);
        policy.ObserveHealthy(301200);
        policy.ObserveUnhealthy();
        policy.ObserveUnhealthy();
        Assert.True(policy.Schedule(policy.Generation, "exit-unavailable", "new outage", 330000));
        Assert.Equal(0, policy.Attempts);
        Assert.Equal(331000, policy.Pending!.DueAt);
        Assert.False(policy.Exhausted);
    }
}

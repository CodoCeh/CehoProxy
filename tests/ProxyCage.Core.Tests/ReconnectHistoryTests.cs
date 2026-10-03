using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class ReconnectHistoryTests
{
    [Fact]
    public void Exposes_actual_trigger_attempt_and_result()
    {
        var history = new ReconnectHistory();
        var seen = new List<string>();
        var attempt = history.Begin("engine-exited", "Engine exited with code 1", new DelegateReport(seen.Add));
        attempt.Phase("Waiting for listeners", waiting: true);
        var current = history.Snapshot().Current!;
        Assert.Equal("engine-exited", current.ReasonCode);
        Assert.Equal("Engine exited with code 1", current.Reason);
        Assert.Equal("running", current.State);
        Assert.Null(current.FinishedUtc);
        Assert.Equal("Waiting for listeners", current.Stage);
        attempt.Retrying(2, 5, "Retrying adapter 2/5");
        Assert.Equal(2, history.Snapshot().Current!.Attempt);
        Assert.Equal(5, history.Snapshot().Current!.MaxAttempts);
        attempt.Complete(false, "Listener readiness timed out");
        var completed = history.Snapshot();
        Assert.Null(completed.Current);
        Assert.Equal("failed", completed.Recent.Single().State);
        Assert.Equal("Listener readiness timed out", completed.Recent.Single().Result);
        Assert.NotNull(completed.Recent.Single().FinishedUtc);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void History_is_bounded_newest_first_and_snapshots_do_not_mutate()
    {
        var history = new ReconnectHistory(capacity: 3);
        for (var i = 0; i < 10; i++) history.Begin("manual", $"Start {i}").Complete(true, "Ready");
        var view = history.Snapshot();
        Assert.Equal(3, view.Recent.Count);
        Assert.Equal(new[] { "Start 9", "Start 8", "Start 7" }, view.Recent.Select(e => e.Reason));
        var latest = history.Begin("network-changed", "Network changed");
        Assert.Equal(3, history.Snapshot().Recent.Count);
        Assert.Equal("Start 9", view.Recent[0].Reason);
        latest.Complete(true, "Recovered");
        latest.Note("Late callback");
        Assert.Equal("Recovered", history.Snapshot().Recent[0].Stage);
    }

    [Fact]
    public void Completion_can_only_be_recorded_once()
    {
        var history = new ReconnectHistory();
        var attempt = history.Begin("service-start", "Service startup");
        attempt.Complete(true, "Ready");
        attempt.Complete(false, "Late failure");
        Assert.Equal("succeeded", history.Snapshot().Recent.Single().State);
        Assert.Equal("Ready", history.Snapshot().Recent.Single().Result);
    }
}

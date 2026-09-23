using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class TunnelRuleApplyTests
{
    [Fact]
    public async Task Restarts_engine_when_start_finishes_while_rules_wait_for_engine_queue()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-rule-race-" + Guid.NewGuid());
        using var startup = EngineMutex.Acquire(root);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = false;
        var restarts = 0;
        var applies = 0;

        var task = Task.Run(() => TunnelRuleApply.RunAsync(
            new DelegateReport(_ => { }),
            () => { waiting.SetResult(); return EngineMutex.Acquire(root); },
            () => running,
            _ => { restarts++; return Task.FromResult<string?>(null); },
            _ => { applies++; return Task.FromResult("applied"); },
            "restarted"));

        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        running = true;
        startup.Dispose();

        Assert.Equal("restarted", await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, restarts);
        Assert.Equal(0, applies);
    }

    [Fact]
    public async Task Applies_rules_without_starting_engine_when_protection_is_off()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-rule-off-" + Guid.NewGuid());
        var restarts = 0;
        var applies = 0;

        var result = await TunnelRuleApply.RunAsync(
            new DelegateReport(_ => { }),
            () => EngineMutex.Acquire(root),
            () => false,
            _ => { restarts++; return Task.FromResult<string?>(null); },
            _ => { applies++; return Task.FromResult("applied"); },
            "restarted");

        Assert.Equal("applied", result);
        Assert.Equal(0, restarts);
        Assert.Equal(1, applies);
    }
}

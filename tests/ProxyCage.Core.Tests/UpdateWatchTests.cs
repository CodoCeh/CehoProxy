using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("UpdateWatch")]
public class UpdateWatchTests
{
    [Fact]
    public async Task A_newer_version_is_remembered_and_an_older_one_is_not_shown()
    {
        UpdateWatch.Reset();
        var calls = 0;
        UpdateWatch.Probe = _ => { calls++; return Task.FromResult<string?>("999.0.0"); };
        UpdateWatch.RefreshInBackground("o/r");
        await WaitAsync(() => UpdateWatch.Available is not null);
        Assert.Equal("999.0.0", UpdateWatch.Available);

        UpdateWatch.RefreshInBackground("o/r");
        await Task.Delay(100);
        Assert.Equal(1, calls);

        UpdateWatch.Reset();
        UpdateWatch.Probe = _ => Task.FromResult<string?>("0.0.1");
        UpdateWatch.RefreshInBackground("o/r");
        await Task.Delay(200);
        Assert.Null(UpdateWatch.Available);
    }

    [Fact]
    public async Task A_failed_check_shows_nothing_and_waits_before_retrying()
    {
        UpdateWatch.Reset();
        var calls = 0;
        UpdateWatch.Probe = _ => { calls++; throw new HttpRequestException("offline"); };
        UpdateWatch.RefreshInBackground("o/r");
        await WaitAsync(() => calls == 1);
        await Task.Delay(100);
        UpdateWatch.RefreshInBackground("o/r");
        await Task.Delay(100);
        Assert.Equal(1, calls);
        Assert.Null(UpdateWatch.Available);
    }

    private static async Task WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++) await Task.Delay(20);
    }
}

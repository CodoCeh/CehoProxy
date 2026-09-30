using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class TrayNotifierTests
{
    private static TrayState.Snapshot On(string country) => new(true, false, true, 1, country, "1.2.3.4", true);
    private static readonly TrayState.Snapshot Off = new(false, false, true, 1, null, null, false);
    private static readonly TrayState.Snapshot Rising = new(false, true, true, 1, null, null, false);
    private static readonly TrayState.Snapshot Healing = new(false, true, true, 1, null, null, false, Recovering: true);

    [Fact]
    public void First_reading_and_a_plain_restart_stay_quiet()
    {
        var n = new TrayNotifier();
        Assert.Null(n.Next("ru", TrayLook.Protected, On("US")));
        Assert.Null(n.Next("ru", TrayLook.Starting, Rising));
        Assert.Null(n.Next("ru", TrayLook.Protected, On("US")));
    }

    [Fact]
    public void Loss_and_return_are_told_once_each()
    {
        var n = new TrayNotifier();
        n.Next("ru", TrayLook.Protected, On("US"));
        Assert.Equal(Strings.T("ru", "state_recovering"), n.Next("ru", TrayLook.Starting, Healing));
        Assert.Null(n.Next("ru", TrayLook.Starting, Healing));
        Assert.Null(n.Next("ru", TrayLook.Off, Off));
        Assert.StartsWith(Strings.T("ru", "state_on"), n.Next("ru", TrayLook.Protected, On("US")));
        Assert.Null(n.Next("ru", TrayLook.Protected, On("US")));
    }

    [Fact]
    public void Turning_off_and_exit_change_are_told()
    {
        var n = new TrayNotifier();
        n.Next("ru", TrayLook.Protected, On("US"));
        Assert.Equal(Strings.T("ru", "notice_exit_changed", "US", "NL"), n.Next("ru", TrayLook.Protected, On("NL")));
        Assert.Equal(Strings.T("ru", "notice_off"), n.Next("ru", TrayLook.Off, Off));
        Assert.Null(n.Next("ru", TrayLook.Stopped, null));
    }
}

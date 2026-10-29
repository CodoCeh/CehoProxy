namespace ProxyCage.Core.Tests;

public sealed class AutostartTests
{
    [Fact]
    public void Windows_autostart_runs_as_system_at_boot()
    {
        var script = Autostart.WindowsTaskScript(
            @"C:\ProgramData\CehoProxy\cehoproxy.exe",
            @"C:\ProgramData\CehoProxy");

        Assert.Contains("New-ScheduledTaskTrigger -AtStartup", script);
        Assert.Contains("-UserId 'SYSTEM' -LogonType ServiceAccount", script);
        Assert.DoesNotContain("-AtLogOn", script);
    }

    [Fact]
    public void Mac_daemon_runs_with_interactive_priority()
    {
        var plist = Autostart.LaunchdPlist(
            "/Library/Application Support/CehoProxy/cehoproxy",
            "/Library/Application Support/CehoProxy");

        Assert.Contains("<key>ProcessType</key><string>Interactive</string>", plist);
    }

    [Fact]
    public void Old_mac_plist_gets_interactive_priority_once()
    {
        const string old = "<plist version=\"1.0\">\n<dict>\n  <key>Label</key><string>ru.codoceh.cehoproxy</string>\n</dict>\n</plist>\n";

        var fixedOnce = Autostart.AddLaunchdProcessType(old);

        Assert.Contains("<key>ProcessType</key><string>Interactive</string>\n</dict>", fixedOnce);
        Assert.Equal(fixedOnce, Autostart.AddLaunchdProcessType(fixedOnce));
    }
}

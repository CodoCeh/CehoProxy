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
}

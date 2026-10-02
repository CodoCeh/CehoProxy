using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class UpdateRelaunchTests
{
    [Fact]
    public void Helper_waits_for_us_to_exit_and_does_not_kill_the_task()
    {
        var script = DaemonControl.WindowsRelaunchScript(
            4242, @"C:\ProgramData\CehoProxy\cehoproxy.exe", @"C:\ProgramData\CehoProxy", autostart: true);

        Assert.Contains("Get-Process -Id $watch", script);
        Assert.Contains("schtasks /run /tn CehoProxy", script);
        Assert.DoesNotContain("schtasks /end", script);
        Assert.Contains("4242", script);
    }

    [Fact]
    public void Without_autostart_the_helper_starts_the_program_itself()
    {
        var script = DaemonControl.WindowsRelaunchScript(
            7, @"C:\ProgramData\CehoProxy\cehoproxy.exe", @"C:\ProgramData\CehoProxy", autostart: false);

        Assert.Contains("Start-Process", script);
        Assert.Contains("daemon", script);
        Assert.DoesNotContain("schtasks /end", script);
        Assert.DoesNotContain("schtasks /run", script);
    }

    [Fact]
    public void Panel_waits_for_the_interface_instead_of_reloading_into_a_dead_port()
    {
        Assert.Contains("go('/?tab=state')", WebUi.JobScript);
        Assert.Contains("function go(url)", WebUi.JobScript);
        Assert.Contains("j.relaunch", WebUi.JobScript);
        Assert.Contains("waitPanel", WebUi.JobScript);
        Assert.Contains("sawDown", WebUi.JobScript);
        Assert.Contains("fails>=3", WebUi.JobScript);
    }

    [Fact]
    public void Live_refresh_swaps_blocks_without_replaying_the_fade_in()
    {
        Assert.Contains("replaceWith(next)", WebUi.StateRefreshScript);
        Assert.Contains("next.style.animation='none'", WebUi.StateRefreshScript);
    }
}

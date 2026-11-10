using System.Diagnostics;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class WindowsProcessQueryTests
{
    [Fact]
    public void Finds_a_child_with_its_command_line_and_owner_without_powershell()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var child = Process.Start(new ProcessStartInfo("ping", "-n 20 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        try
        {
            var watch = Stopwatch.StartNew();
            var found = Os.WindowsProcessesOwned("ping.exe");
            watch.Stop();

            var mine = Assert.Single(found, p => p.Pid == child.Id);
            Assert.Contains("-n 20 127.0.0.1", mine.CommandLine);
            Assert.False(string.IsNullOrWhiteSpace(mine.User));
            Assert.True(watch.ElapsedMilliseconds < 1000, $"поиск занял {watch.ElapsedMilliseconds} мс");
        }
        finally { child.Kill(); }
    }

    [Fact]
    public void A_missing_process_gives_an_empty_list_and_other_systems_are_left_alone()
    {
        Assert.Empty(Os.WindowsProcessesOwned("no-such-process-ceho.exe"));
    }
}

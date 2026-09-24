using System.Diagnostics;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class UnixHelperStartInfoTests
{
    [Fact]
    public void SystemdUpdateHelperRunsInSeparateTransientUnit()
    {
        var start = DaemonControl.CreateSystemdUpdateStartInfo(
            "/var/lib/cehoproxy/update relaunch.sh", "/var/lib/cehoproxy", 4242);

        Assert.Equal("systemd-run", start.FileName);
        Assert.Equal("/var/lib/cehoproxy", start.WorkingDirectory);
        Assert.Equal(new[]
        {
            "--unit=cehoproxy-update-4242", "--collect", "/bin/sh",
            "/var/lib/cehoproxy/update relaunch.sh"
        }, start.ArgumentList);
    }

    [Fact]
    public void LaunchdUpdateHelperIsSubmittedAsSeparateJob()
    {
        var start = DaemonControl.CreateLaunchdUpdateStartInfo(
            "/Library/Application Support/CehoProxy/update relaunch.sh",
            "/Library/Application Support/CehoProxy", "ru.codoceh.cehoproxy.update.4242");

        Assert.Equal("launchctl", start.FileName);
        Assert.Equal(new[]
        {
            "submit", "-l", "ru.codoceh.cehoproxy.update.4242", "-p", "/bin/sh",
            "--", "/bin/sh", "/Library/Application Support/CehoProxy/update relaunch.sh"
        }, start.ArgumentList);
    }

    [Fact]
    public async Task HelperPathWithSpacesIsPassedAsOneArgument()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "ceho helper " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var helper = Path.Combine(root, "update relaunch.sh");
            var marker = Path.Combine(root, "helper ran.txt");
            File.WriteAllText(helper, $"#!/bin/sh\nprintf ran > '{marker}'\n");

            using var process = Process.Start(DaemonControl.CreateUnixHelperStartInfo(helper, root))
                ?? throw new InvalidOperationException("Could not start Unix helper process.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, process.ExitCode);
            Assert.Equal("ran", File.ReadAllText(marker));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

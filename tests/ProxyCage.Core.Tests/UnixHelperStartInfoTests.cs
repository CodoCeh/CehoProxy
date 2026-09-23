using System.Diagnostics;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class UnixHelperStartInfoTests
{
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

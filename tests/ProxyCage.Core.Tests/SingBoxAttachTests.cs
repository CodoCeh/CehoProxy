using System.Diagnostics;
using ProxyCage.Core;
using Xunit;

namespace ProxyCage.Core.Tests;

public class SingBoxAttachTests
{
    [Fact]
    public void Attached_process_is_seen_running_and_can_be_stopped()
    {
        var program = OperatingSystem.IsWindows() ? "ping" : "sleep";
        var arguments = OperatingSystem.IsWindows() ? "-n 60 127.0.0.1" : "60";
        using var foreign = Process.Start(new ProcessStartInfo(program, arguments)
        { UseShellExecute = false, CreateNoWindow = true })!;

        using var attached = new SingBoxProcess();
        attached.Attach(foreign.Id);

        Assert.True(attached.Attached);
        Assert.Equal((uint)foreign.Id, attached.ProcessId);
        Assert.True(attached.IsRunning);

        attached.Stop(3000);

        Assert.True(foreign.WaitForExit(5000));
        Assert.False(attached.IsRunning);
    }
}

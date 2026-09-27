namespace ProxyCage.Core.Tests;

internal static class TestEngine
{
    public static void Place(string root)
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(root, Os.EngineFileName);
        File.WriteAllText(path, $"#!/bin/sh\necho \"sing-box version {Installer.EngineVersion}\"\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}

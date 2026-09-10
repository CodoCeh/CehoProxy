using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class InstallerCronetTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "chp-cronet-" + Guid.NewGuid().ToString("N")[..8]);

    public InstallerCronetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Missing_cronet_when_engine_without_dll()
    {
        File.WriteAllText(Path.Combine(_root, Os.EngineFileName), "engine");
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(Installer.MissingCronetDll(_root));
            return;
        }

        Assert.True(Installer.MissingCronetDll(_root));
    }

    [Fact]
    public void Not_missing_when_cronet_is_present()
    {
        File.WriteAllText(Path.Combine(_root, Os.EngineFileName), "engine");
        File.WriteAllText(Path.Combine(_root, Installer.CronetFileName), "dll");
        Assert.False(Installer.MissingCronetDll(_root));
    }

    [Fact]
    public void Copy_cronet_from_source_directory()
    {
        if (!OperatingSystem.IsWindows()) return;

        var src = Path.Combine(_root, "bundle");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, Installer.CronetFileName), "cronet");

        Installer.CopyCronetDependencies(src, _root);

        Assert.Equal("cronet", File.ReadAllText(Path.Combine(_root, Installer.CronetFileName)));
    }

    [Fact]
    public void Copy_cronet_same_directory_does_not_throw()
    {
        if (!OperatingSystem.IsWindows()) return;

        var dll = Path.Combine(_root, Installer.CronetFileName);
        File.WriteAllText(dll, "already-here");

        Installer.CopyCronetDependencies(_root, _root);

        Assert.Equal("already-here", File.ReadAllText(dll));
    }

    [Fact]
    public void Copy_cronet_keeps_locked_destination()
    {
        if (!OperatingSystem.IsWindows()) return;

        var src = Path.Combine(_root, "bundle");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, Installer.CronetFileName), "new");
        var dest = Path.Combine(_root, Installer.CronetFileName);
        File.WriteAllText(dest, "old");

        using (new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None))
            Installer.CopyCronetDependencies(src, _root);

        Assert.Equal("old", File.ReadAllText(dest));
    }
}

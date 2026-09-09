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
}

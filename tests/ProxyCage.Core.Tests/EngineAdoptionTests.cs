namespace ProxyCage.Core.Tests;

public class EngineAdoptionTests
{
    [Fact]
    public void System_engine_is_copied_into_the_program_folder()
    {
        if (Os.IsWindows) return;
        var (root, system) = Folders();
        try
        {
            var found = Path.Combine(system, Os.SingBoxFileName);
            File.WriteAllText(found, "#!/bin/sh\nexit 0\n");
            var said = new List<string>();

            var engine = Installer.AdoptSystemEngine(root, said.Add, "ru", [system]);

            Assert.Equal(Path.Combine(root, Os.EngineFileName), engine);
            Assert.True(File.Exists(engine));
            Assert.Contains(found, said.Single());
            Assert.True(File.GetUnixFileMode(engine!).HasFlag(UnixFileMode.UserExecute));
        }
        finally { Clean(root, system); }
    }

    [Fact]
    public void Own_engine_wins_and_nothing_is_copied()
    {
        if (Os.IsWindows) return;
        var (root, system) = Folders();
        try
        {
            var own = Path.Combine(root, Os.EngineFileName);
            File.WriteAllText(own, "own");
            File.WriteAllText(Path.Combine(system, Os.SingBoxFileName), "system");
            var said = new List<string>();

            var engine = Installer.AdoptSystemEngine(root, said.Add, "ru", [system]);

            Assert.Equal(own, engine);
            Assert.Equal("own", File.ReadAllText(own));
            Assert.Empty(said);
        }
        finally { Clean(root, system); }
    }

    [Fact]
    public void Without_a_system_engine_nothing_happens()
    {
        if (Os.IsWindows) return;
        var (root, system) = Folders();
        try
        {
            var said = new List<string>();

            Assert.Null(Installer.AdoptSystemEngine(root, said.Add, "ru", [system]));
            Assert.Empty(said);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally { Clean(root, system); }
    }

    private static (string Root, string System) Folders()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "ceho-engine-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(baseDir, "program");
        var system = Path.Combine(baseDir, "usr-local-bin");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(system);
        return (root, system);
    }

    private static void Clean(string root, string system)
    {
        try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); } catch { }
    }
}

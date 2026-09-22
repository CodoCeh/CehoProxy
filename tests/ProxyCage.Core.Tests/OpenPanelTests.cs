using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class OpenPanelTests
{
    [Fact]
    public void Readable_config_port_wins_over_the_pointer()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "config.json"), """{"WebPort":8901}""");
            Auth.WritePanelPointer(root, 8902, 2082);

            Assert.Equal(8901, CehoConfig.ReadWebPort(Path.Combine(root, "config.json"), root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Unreadable_config_uses_the_panel_pointer()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = TempRoot();
        var config = Path.Combine(root, "config.json");
        try
        {
            File.WriteAllText(config, """{"WebPort":8901}""");
            File.SetUnixFileMode(config, UnixFileMode.None);
            Auth.WritePanelPointer(root, 8902, 2082);

            Assert.Equal(8902, CehoConfig.ReadWebPort(config, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Missing_config_and_pointer_use_the_default_port()
    {
        var root = TempRoot();
        try
        {
            Assert.Equal(new CehoConfig().WebPort, CehoConfig.ReadWebPort(Path.Combine(root, "config.json"), root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Terminal_start_goes_to_the_background_and_a_service_stays_in_front()
    {
        Assert.True(DaemonControl.WantsBackground(inputRedirected: false, foregroundMarker: null));
        Assert.False(DaemonControl.WantsBackground(inputRedirected: true, foregroundMarker: null));
        Assert.False(DaemonControl.WantsBackground(inputRedirected: false, foregroundMarker: "1"));
    }

    [Fact]
    public void Short_command_lands_in_the_given_directory()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = TempRoot();
        var target = Path.Combine(root, "cehoproxy");
        File.WriteAllText(target, "#!/bin/sh\n");
        var bin = Path.Combine(root, "bin");
        try
        {
            Installer.LinkShortCommand(bin, target);
            var linked = File.ResolveLinkTarget(Path.Combine(bin, "chp"), returnFinalTarget: true);
            Assert.NotNull(linked);
            Assert.Equal(Path.GetFullPath(target), linked.FullName);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

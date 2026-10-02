using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class ElevationTests
{
    [Fact]
    public void Installing_and_removing_need_real_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-rights-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(RightsNeed.Root, Elevation.Needed(new[] { "install" }, root));
        Assert.Equal(RightsNeed.Root, Elevation.Needed(new[] { "uninstall" }, root));
        Assert.Equal(RightsNeed.Root, Elevation.Needed(new[] { "daemon" }, root));
        Assert.Equal(RightsNeed.Root, Elevation.Needed(new[] { "autostart", "on" }, root));
    }

    [Fact]
    public void Reading_the_state_needs_nothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-rights-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(RightsNeed.None, Elevation.Needed(new[] { "status" }, root));
        Assert.Equal(RightsNeed.None, Elevation.Needed(new[] { "autostart" }, root));
        Assert.Equal(RightsNeed.None, Elevation.Needed(Array.Empty<string>(), root));
    }

    [Fact]
    public void Update_and_engine_only_need_a_writable_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-rights-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(RightsNeed.FolderWrite, Elevation.Needed(new[] { "update" }, root));
            Assert.Equal(RightsNeed.FolderWrite, Elevation.Needed(new[] { "engine" }, root));
            Assert.True(Elevation.Satisfied(RightsNeed.FolderWrite, root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_folder_we_cannot_write_to_is_not_enough()
    {
        if (OperatingSystem.IsWindows() || Os.IsElevated()) return;

        Assert.False(Elevation.Satisfied(RightsNeed.FolderWrite, "/System/ceho-not-writable"));
    }

    [Fact]
    public void Windows_never_pretends_it_can_ask_for_a_password()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? RightsAsk.Window : RightsAsk.None, Elevation.Way(OsKind.Windows, hasTerminal: true));
        Assert.Equal(RightsAsk.None, Elevation.Way(OsKind.Windows, hasTerminal: false));
    }

    [Fact]
    public void Without_a_terminal_macOS_asks_in_a_window()
    {
        Assert.Equal(RightsAsk.Window, Elevation.Way(OsKind.Mac, hasTerminal: false));
    }

    [Fact]
    public void Quoting_survives_apostrophes_and_spaces()
    {
        Assert.Equal("'/tmp/my dir/chp'", Elevation.ShellQuote("/tmp/my dir/chp"));
        Assert.Equal("'it'\\''s'", Elevation.ShellQuote("it's"));
    }

    [Fact]
    public void Sudo_gets_the_full_path_and_keeps_our_home()
    {
        var args = Elevation.SudoArguments("/usr/local/bin/cehoproxy", new[] { "update", "--yes" }, "/opt/ceho home");

        Assert.Equal(new[]
        {
            "--", "env", "CEHOPROXY_HOME=/opt/ceho home",
            "/usr/local/bin/cehoproxy", "update", "--yes",
        }, args);
    }

    [Fact]
    public void Sudo_without_a_custom_home_passes_only_the_command()
    {
        var args = Elevation.SudoArguments("/usr/local/bin/cehoproxy", new[] { "daemon" }, null);

        Assert.Equal(new[] { "--", "/usr/local/bin/cehoproxy", "daemon" }, args);
    }

    [Fact]
    public void The_password_window_runs_a_quoted_detached_command()
    {
        var script = Elevation.AppleScript("/Library/Application Support/CehoProxy/cehoproxy",
            new[] { "update", "--yes" }, null);

        Assert.StartsWith("do shell script \"", script);
        Assert.EndsWith("with administrator privileges", script);
        Assert.Contains("'/Library/Application Support/CehoProxy/cehoproxy' 'update' '--yes'", script);
        Assert.Contains("</dev/null >/dev/null 2>&1", script);
    }

    [Fact]
    public void The_password_window_escapes_quotes_for_applescript()
    {
        var script = Elevation.AppleScript("/tmp/ceho\"odd/chp", new[] { "engine" }, null);

        Assert.Contains("\\\"", script);
        Assert.DoesNotContain("/tmp/ceho\"odd", script);
    }

    [Fact]
    public void Missing_rights_come_with_a_button_not_just_advice()
    {
        if (Os.IsElevated()) return;
        var root = Path.Combine(Path.GetTempPath(), "ceho-el-" + Guid.NewGuid().ToString("N")[..8]);
        var checks = Preflight.Run(new CehoConfig(), root);
        Assert.Contains(checks, c => c.Level == Preflight.Level.Blocker && c.Repair == Repair.Elevate);
        Assert.False(new Doctor.Result(checks.Where(c => c.Repair == Repair.Elevate).ToList(), [], []).Fixable);
    }

    [Theory]
    [InlineData("Tailscale", true)]
    [InlineData("ZeroTier One", true)]
    [InlineData("Happ", false)]
    public void Only_known_harmless_overlays_are_not_a_warning(string name, bool harmless) =>
        Assert.Equal(harmless, Doctor.IsHarmlessOverlay(name));
}

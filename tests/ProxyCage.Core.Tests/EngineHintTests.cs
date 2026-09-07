using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Про отсутствующий движок человеку говорят ровно одну команду. Тексты легко
/// разъезжаются при правках, поэтому команда проверяется, а не читается глазами.
/// </summary>
public class EngineHintTests
{
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Preflight_names_the_short_command(string lang)
    {
        var win = Strings.T(lang, "pf_engine_fix_win", "sing-box.exe");
        var unix = Strings.T(lang, "pf_engine_fix_unix", "/var/lib/cehoproxy");

        Assert.Contains("chp engine", win);
        Assert.Contains("sudo chp engine", unix);
        Assert.DoesNotContain("--with-engine", win);
        Assert.DoesNotContain("--with-engine", unix);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Installer_offers_the_same_command(string lang)
    {
        Assert.Contains("sudo chp engine", Strings.T(lang, "engine_command", "sudo "));
        Assert.Contains("chp engine", Strings.T(lang, "engine_command", ""));
        Assert.Contains("chp engine update", Strings.T(lang, "engine_update_hint", ""));
        Assert.Contains("chp engine", Strings.T(lang, "engine_need_rights", ""));
    }

    [Fact]
    public void Writable_check_answers_for_the_folder_itself()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chp-w-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Assert.True(Preflight.FolderIsWritable(dir, out var why));
            Assert.Equal("", why);
            Assert.False(Directory.EnumerateFiles(dir).Any());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Unwritable_folder_explains_itself()
    {
        if (Os.IsWindows) return;

        var parent = Path.Combine(Path.GetTempPath(), "chp-ro-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(parent);
        Os.Run("chmod", $"500 {parent}", 5000);
        try
        {
            Assert.False(Preflight.FolderIsWritable(Path.Combine(parent, "inside"), out var why));
            Assert.NotEqual("", why);
        }
        finally
        {
            Os.Run("chmod", $"700 {parent}", 5000);
            try { Directory.Delete(parent, true); } catch { }
        }
    }
}

using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class IsolatedAppBounceTests
{
    private static AppEntry Telegram() => new()
    {
        Name = "Telegram Desktop",
        Folder = @"C:\Users\s.bonich\AppData\Roaming\Telegram Desktop",
    };

    private static AppEntry Cursor() => new()
    {
        Name = "Cursor",
        Folder = @"C:\Users\s.bonich\AppData\Local\Programs\cursor",
    };

    private static AppEntry Chrome() => new()
    {
        Name = "Google Chrome",
        Folder = @"C:\Program Files\Google\Chrome\Application",
    };

    [Fact]
    public void Telegram_desktop_is_the_sticky_client_to_rebounce()
    {
        Assert.True(AppDetector.IsTelegram(Telegram()));
        Assert.Equal(new[] { "telegram" }, IsolatedAppBounce.ProcessNames(Telegram()));
    }

    [Fact]
    public void Cursor_and_chrome_are_not_treated_as_telegram()
    {
        Assert.False(AppDetector.IsTelegram(Cursor()));
        Assert.False(AppDetector.IsTelegram(Chrome()));
        Assert.Equal(new[] { "chrome" }, IsolatedAppBounce.ProcessNames(Chrome()));
        Assert.Empty(IsolatedAppBounce.ProcessNames(Cursor()));
    }

    [Fact]
    public void Sticky_tcp_reset_is_for_listed_clients_not_browsers()
    {
        Assert.True(IsolatedAppBounce.UsesStickyTcpReset(Telegram()));
        Assert.True(IsolatedAppBounce.UsesStickyTcpReset(Cursor()));
        Assert.True(IsolatedAppBounce.UsesStickyTcpReset(new AppEntry
        {
            Name = "probe-app",
            Folder = @"C:\CehoLab\probe-app",
        }));
        Assert.False(IsolatedAppBounce.UsesStickyTcpReset(Chrome()));
        Assert.False(IsolatedAppBounce.UsesStickyTcpReset(new AppEntry
        {
            Name = "Opera",
            Folder = @"C:\Users\Administrator\AppData\Local\Programs\Opera",
        }));
        Assert.False(IsolatedAppBounce.UsesStickyTcpReset(new AppEntry
        {
            Name = "Telegram Desktop",
            Folder = @"C:\Users\s.bonich\AppData\Roaming\Telegram Desktop",
            Enabled = false,
        }));
    }

    [Fact]
    public void Direct_system_tools_are_not_reset_when_apps_are_isolated()
    {
        var cfg = new CehoConfig
        {
            Apps =
            {
                Telegram(),
                Cursor(),
                Chrome(),
            },
        };

        Assert.True(IsolatedAppBounce.WouldResetProcess(
            cfg, @"C:\Users\s.bonich\AppData\Roaming\Telegram Desktop\Telegram.exe"));
        Assert.True(IsolatedAppBounce.WouldResetProcess(
            cfg, @"C:\Users\s.bonich\AppData\Local\Programs\cursor\Cursor.exe"));
        Assert.False(IsolatedAppBounce.WouldResetProcess(
            cfg, @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.False(IsolatedAppBounce.WouldResetProcess(cfg, @"C:\Windows\System32\curl.exe"));
        Assert.False(IsolatedAppBounce.WouldResetProcess(cfg, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"));
        Assert.False(IsolatedAppBounce.WouldResetProcess(cfg, @"C:\Windows\System32\OpenSSH\sshd.exe"));
        Assert.False(IsolatedAppBounce.WouldResetProcess(cfg, @"C:\Windows\System32\svchost.exe"));
    }

    [Fact]
    public void Own_engine_and_unlisted_apps_are_never_reset()
    {
        var cfg = new CehoConfig { Apps = { Telegram() } };

        Assert.True(IsolatedAppBounce.IsProtectedImage(@"C:\ProgramData\CehoProxy\cehoproxy.exe"));
        Assert.True(IsolatedAppBounce.IsProtectedImage(@"C:\ProgramData\CehoProxy\ceho-engine.exe"));
        Assert.True(IsolatedAppBounce.IsProtectedImage(@"C:\Services\codex-proxy\sing-box.exe"));
        Assert.False(IsolatedAppBounce.WouldResetProcess(cfg, @"C:\ProgramData\CehoProxy\cehoproxy.exe"));
        Assert.False(IsolatedAppBounce.WouldResetProcess(
            cfg, @"C:\Users\s.bonich\AppData\Local\Programs\cursor\Cursor.exe"));
    }

    [Fact]
    public void Telegram_exe_in_the_roaming_folder_matches_the_isolation_regex()
    {
        var rxes = AppDetector.ToRegexes(Telegram());
        var exe = @"C:\Users\s.bonich\AppData\Roaming\Telegram Desktop\Telegram.exe";
        var other = @"C:\Users\other\AppData\Roaming\Telegram Desktop\Telegram.exe";

        Assert.Contains(rxes, r => System.Text.RegularExpressions.Regex.IsMatch(exe, r));
        Assert.Contains(rxes, r => System.Text.RegularExpressions.Regex.IsMatch(other, r));
    }

    [Fact]
    public void Updater_is_not_a_client_to_reset()
    {
        Assert.True(IsolatedAppBounce.IsTelegramUpdater(
            @"C:\Users\s.bonich\AppData\Roaming\Telegram Desktop\Updater.exe"));
        Assert.False(IsolatedAppBounce.IsTelegramUpdater(
            @"C:\Users\s.bonich\AppData\Roaming\Telegram Desktop\Telegram.exe"));
        Assert.False(IsolatedAppBounce.IsTelegramUpdater(null));
    }

    [Fact]
    public void Old_direct_tcp_is_dropped_loopback_and_tunnel_are_kept()
    {
        const string tun = "172.31.211.";

        Assert.True(IsolatedAppBounce.ShouldResetConnection("192.168.1.40", "149.154.167.91", tun));
        Assert.False(IsolatedAppBounce.ShouldResetConnection("172.31.211.2", "149.154.167.91", tun));
        Assert.False(IsolatedAppBounce.ShouldResetConnection("127.0.0.1", "127.0.0.1", tun));
        Assert.False(IsolatedAppBounce.ShouldResetConnection("192.168.1.40", "127.0.0.1", tun));
        Assert.False(IsolatedAppBounce.ShouldResetConnection("::1", "149.154.167.91", tun));
    }

    [Fact]
    public void Telegram_regexes_cover_every_windows_user_profile()
    {
        var rxes = AppDetector.ToRegexes(Telegram());
        var otherRoaming = @"C:\Users\bsv\AppData\Roaming\Telegram Desktop\Telegram.exe";
        var programFiles = @"C:\Program Files\Telegram Desktop\Telegram.exe";

        Assert.Contains(rxes, r => System.Text.RegularExpressions.Regex.IsMatch(otherRoaming, r));
        Assert.Contains(rxes, r => System.Text.RegularExpressions.Regex.IsMatch(programFiles, r));
        Assert.DoesNotContain(rxes, r => System.Text.RegularExpressions.Regex.IsMatch(
            @"C:\Users\bsv\AppData\Local\Programs\cursor\Cursor.exe", r));
    }

}

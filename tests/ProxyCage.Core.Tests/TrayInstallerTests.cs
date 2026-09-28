using System.Runtime.InteropServices;

namespace ProxyCage.Core.Tests;

public class TrayInstallerTests
{
    [Theory]
    [InlineData(OsKind.Windows, Architecture.X64, "cehoproxy-tray-win-x64.exe")]
    [InlineData(OsKind.Mac, Architecture.Arm64, "cehoproxy-tray-osx-arm64.zip")]
    [InlineData(OsKind.Mac, Architecture.X64, "cehoproxy-tray-osx-x64.zip")]
    [InlineData(OsKind.Linux, Architecture.X64, "cehoproxy-tray-linux-x64")]
    [InlineData(OsKind.Linux, Architecture.Arm64, "cehoproxy-tray-linux-arm64")]
    public void Each_system_downloads_its_own_icon_file(OsKind kind, Architecture arch, string asset)
    {
        Assert.Equal(asset, TrayInstaller.AssetName(kind, arch));
    }

    [Fact]
    public void Icon_is_taken_from_the_release_of_the_same_version()
    {
        Assert.Equal(
            "https://github.com/CodoCeh/CehoProxy/releases/download/v1.2.77/cehoproxy-tray-linux-x64",
            TrayInstaller.DownloadUrl("CodoCeh/CehoProxy", "1.2.77", "cehoproxy-tray-linux-x64"));
    }

    [Fact]
    public void Mac_icon_version_is_read_from_its_bundle()
    {
        const string plist = """
            <dict>
              <key>CFBundleName</key><string>CehoProxy Tray</string>
              <key>CFBundleShortVersionString</key><string>1.2.76</string>
            </dict>
            """;

        Assert.Equal("1.2.76", TrayInstaller.PlistVersion(plist));
        Assert.Null(TrayInstaller.PlistVersion("<dict></dict>"));
    }

    [Theory]
    [InlineData("1.2.77+4bef1bc", "1.2.77")]
    [InlineData("1.2.77.0", "1.2.77")]
    [InlineData("1.2.77\n", "1.2.77")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Icon_version_is_compared_without_build_details(string? raw, string? plain)
    {
        Assert.Equal(plain, TrayInstaller.PlainVersion(raw));
    }

    [Fact]
    public void Linux_autostart_starts_the_icon_from_the_install_folder()
    {
        var entry = TrayInstaller.LinuxAutostart("/var/lib/cehoproxy/cehoproxy-tray");

        Assert.Contains("[Desktop Entry]", entry);
        Assert.Contains("Exec=\"/var/lib/cehoproxy/cehoproxy-tray\"", entry);
        Assert.Contains("NoDisplay=true", entry);
    }
}

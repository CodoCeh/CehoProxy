using System.Net;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class LeakGuardTests
{
    [Theory]
    [InlineData("172.31.211.1/30", "0.0.0.0-172.31.210.255,172.31.211.4-255.255.255.255,")]
    [InlineData("10.0.0.1/8", "0.0.0.0-9.255.255.255,11.0.0.0-255.255.255.255,")]
    public void Local_ranges_cover_everything_but_the_tun_network(string tun, string ipv4) =>
        Assert.StartsWith(ipv4, LeakGuard.OutsideTun(tun));

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("104.18.32.47", true)]
    [InlineData("172.67.1.1", true)]
    [InlineData("2606:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.5.5", false)]
    [InlineData("192.168.0.114", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("100.100.1.1", false)]
    [InlineData("224.0.0.251", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    public void Only_public_internet_is_blocked(string address, bool blocked) =>
        Assert.Equal(blocked, InRanges(IPAddress.Parse(address), LeakGuard.PublicRemote));

    [Fact]
    public void An_app_pointing_at_a_system_folder_is_recognised()
    {
        var system = Os.IsWindows ? @"C:\Program Files\WindowsApps" : "/usr/bin";
        var own = Os.IsWindows ? @"C:\Users\me\AppData\Local\Programs\cursor" : "/Applications/Cursor.app";

        Assert.True(AppDetector.CoversSystemFolder(new AppEntry { Folder = system }));
        Assert.False(AppDetector.CoversSystemFolder(new AppEntry { Folder = system, SingleFile = true }));
        Assert.False(AppDetector.CoversSystemFolder(new AppEntry { Folder = own }));
    }

    private static bool InRanges(IPAddress ip, string ranges) =>
        ranges.Split(',').Select(r => r.Split('-')).Any(r =>
        {
            var from = IPAddress.Parse(r[0]);
            var to = IPAddress.Parse(r[1]);
            if (from.AddressFamily != ip.AddressFamily) return false;
            var x = new System.Numerics.BigInteger(ip.GetAddressBytes(), isUnsigned: true, isBigEndian: true);
            return x >= new System.Numerics.BigInteger(from.GetAddressBytes(), true, true)
                && x <= new System.Numerics.BigInteger(to.GetAddressBytes(), true, true);
        });
}

using System.Net;
using System.Net.Sockets;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class PhysicalBindTests
{
    /// <summary>
    /// VirtualBox NAT даёт гостю 10.0.2.15 — это физический NIC, не TUN.
    /// Раньше весь 10.x отсекался и inet4_bind_address не попадал в singbox.json → DNS loop.
    /// </summary>
    [Theory]
    [InlineData("10.0.2.15", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("172.31.211.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.32.0.1", false)]
    public void LooksLikeTunnelAddress_classifies_ranges(string address, bool expectedTunnel)
    {
        Assert.Equal(expectedTunnel, Os.LooksLikeTunnelAddress(address));
    }

    [Fact]
    public void Physical_bind_address_is_not_loopback_or_tun()
    {
        var addr = Os.PhysicalBindAddress(CehoConfig.DefaultTunAddress);
        if (addr is null) return;

        Assert.Equal(AddressFamily.InterNetwork, addr.AddressFamily);
        Assert.False(addr.ToString().StartsWith("127.", StringComparison.Ordinal));
        Assert.False(addr.ToString().StartsWith("172.31.211.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Direct_dns_still_resolves_public_host()
    {
        var addresses = await DirectDnsResolver.ResolveAsync(
            "one.one.one.one", tunAddress: CehoConfig.DefaultTunAddress);
        Assert.NotEmpty(addresses);
    }

    [Theory]
    [InlineData("100.100.100.100", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("198.18.0.2", true)]
    [InlineData("198.19.255.1", true)]
    [InlineData("198.20.0.1", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("10.0.0.1", false)]
    public void IsOverlayResolver_catches_tailscale_and_fake_ip_ranges(string address, bool expected) =>
        Assert.Equal(expected, Os.IsOverlayResolver(address));
}

using System.Net;
using System.Net.Sockets;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class PhysicalBindTests
{
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
}

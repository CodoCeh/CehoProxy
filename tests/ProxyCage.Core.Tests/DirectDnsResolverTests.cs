using System.Net;
using System.Net.Sockets;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class DirectDnsResolverTests
{
    [Fact]
    public async Task Resolves_public_host_via_direct_dns()
    {
        var addresses = await DirectDnsResolver.ResolveAsync("one.one.one.one");
        Assert.NotEmpty(addresses);
        Assert.All(addresses, a => Assert.Equal(AddressFamily.InterNetwork, a.AddressFamily));
    }

    [Fact]
    public async Task Literal_ip_passes_through()
    {
        var addresses = await DirectDnsResolver.ResolveAsync("1.1.1.1");
        Assert.Single(addresses);
        Assert.Equal("1.1.1.1", addresses[0].ToString());
    }

    [Fact]
    public async Task Unknown_host_throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(
            () => DirectDnsResolver.ResolveAsync("this-host-should-not-exist-7f3a9.invalid"));
    }
}

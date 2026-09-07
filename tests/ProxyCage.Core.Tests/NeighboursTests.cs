using ProxyCage.Core;
using Xunit;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Живой случай: на машине рядом с нами стоял Happ на той же Wintun. Наша уборка следов
/// сносила его адаптер, Happ поднимался заново уже без своего прокси, а браузер оставался
/// настроен на мёртвый порт — и выглядело это как «cehoproxy убил интернет».
/// </summary>
public class NeighboursTests
{
    private static (string Name, string InstanceId) Wintun(string name) =>
        (name, @"SWD\WINTUN\{0DCCC63E-5622-3880-1E09-7CC9C46AD7B4}");

    [Fact]
    public void Cleanup_never_touches_a_tunnel_of_another_client()
    {
        var seen = new List<string>();
        var mine = TunCleanup.Mine(
            new[] { Wintun("happ-tun"), Wintun(TunCleanup.InterfaceName) },
            ourInterface: null,
            seen.Add);

        Assert.Equal(new[] { TunCleanup.InterfaceName }, mine.Select(a => a.Name));
        Assert.Contains(seen, m => m.Contains("happ-tun"));
    }

    [Fact]
    public void Cleanup_finds_our_old_tunnel_by_interface_name()
    {
        var mine = TunCleanup.Mine(
            new[] { Wintun("happ-tun"), Wintun("tun0") },
            ourInterface: "tun0");

        Assert.Equal(new[] { "tun0" }, mine.Select(a => a.Name));
    }

    [Fact]
    public void Cleanup_ignores_adapters_that_are_not_tunnels()
    {
        var mine = TunCleanup.Mine(
            new[] { (TunCleanup.InterfaceName, @"PCI\VEN_8086&DEV_51F0") },
            ourInterface: null);

        Assert.Empty(mine);
    }

    [Fact]
    public void Our_interface_is_found_by_its_address()
    {
        // Петля есть на любой машине, поэтому проверка честная и на маке, и на Windows.
        Assert.NotNull(TunCleanup.InterfaceWithAddress("127.0.0.1/8"));
        Assert.Null(TunCleanup.InterfaceWithAddress("203.0.113.7/32"));
    }

    [Fact]
    public void Dead_system_proxy_is_named_with_host_and_port()
    {
        var dead = SystemProxy.DeadAmong("0x1", "socks=127.0.0.1:10808", ourPort: 2080, _ => false);
        Assert.Equal("127.0.0.1:10808", dead);
    }

    [Fact]
    public void A_live_or_disabled_proxy_is_not_a_complaint()
    {
        Assert.Null(SystemProxy.DeadAmong("0x0", "socks=127.0.0.1:10808", 2080, _ => false));
        Assert.Null(SystemProxy.DeadAmong("0x1", "socks=127.0.0.1:10808", 2080, _ => true));
        Assert.Null(SystemProxy.DeadAmong("0x1", null, 2080, _ => false));
    }

    [Fact]
    public void Our_own_proxy_port_is_left_to_other_checks()
    {
        Assert.Null(SystemProxy.DeadAmong("0x1", "socks=127.0.0.1:2080", ourPort: 2080, _ => false));
    }

    [Fact]
    public void A_proxy_on_another_machine_is_none_of_our_business()
    {
        Assert.Null(SystemProxy.DeadAmong("0x1", "http=10.0.0.5:3128", 2080, _ => false));
    }

    [Fact]
    public void Several_proxies_in_one_line_are_all_examined()
    {
        var dead = SystemProxy.DeadAmong(
            "1", "http=127.0.0.1:2080;socks=127.0.0.1:10808", ourPort: 2080, port => port == 2080);

        Assert.Equal("127.0.0.1:10808", dead);
    }
}

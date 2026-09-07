using ProxyCage.Core;
using Xunit;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Живой случай: на машине рядом с нами стоял Happ на той же Wintun. Наша уборка следов
/// сносила его работающий туннель, Happ поднимался заново уже без своего прокси, а браузер
/// оставался настроен на мёртвый порт — и выглядело это как «cehoproxy убил интернет».
/// </summary>
public class NeighboursTests
{
    private const string Alien = @"SWD\Wintun\{830F7916-3A43-C3E7-CDDE-4E56048896B0}";
    private const string Ours = @"SWD\Wintun\{0E62885F-8AB7-F41A-DDA4-80DC8653A8F1}";

    private static Func<string, TunCleanup.Nic?> Adapters(params (string Id, TunCleanup.Nic? Nic)[] map) =>
        id => map.FirstOrDefault(m => m.Id == id).Nic;

    [Fact]
    public void A_working_tunnel_of_another_client_is_never_touched()
    {
        var seen = new List<string>();
        var removable = TunCleanup.Removable(
            new[] { Alien, Ours },
            Adapters(
                (Alien, new TunCleanup.Nic("happ-tun", Up: true, Ours: false)),
                (Ours, new TunCleanup.Nic("tun0", Up: true, Ours: true))),
            seen.Add);

        Assert.Equal(new[] { Ours }, removable.Select(a => a.InstanceId));
        Assert.Contains(seen, m => m.Contains("happ-tun"));
    }

    [Fact]
    public void A_dead_adapter_is_removed_whoever_left_it()
    {
        // Трафика за ним нет, а свой туннель поднять он мешает: движок упирается
        // в «файл уже существует». Клиент, которому он нужен, создаст его заново.
        var removable = TunCleanup.Removable(
            new[] { Alien },
            Adapters((Alien, new TunCleanup.Nic("happ-tun", Up: false, Ours: false))));

        Assert.Single(removable);
    }

    [Fact]
    public void A_device_left_without_an_adapter_is_removed_too()
    {
        // Такие оставляет неудачная попытка запуска движка: адаптера уже нет, а устройство
        // ещё держит имя и GUID.
        var removable = TunCleanup.Removable(new[] { Ours }, Adapters());

        Assert.Equal(new[] { Ours }, removable.Select(a => a.InstanceId));
    }

    [Fact]
    public void Devices_that_are_not_tunnels_are_out_of_scope()
    {
        var removable = TunCleanup.Removable(
            new[] { @"PCI\VEN_8086&DEV_51F0&SUBSYS_02448086&REV_01\{0E62885F}" }, Adapters());

        Assert.Empty(removable);
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
    public void A_warning_is_never_swallowed_by_the_verdict()
    {
        var report = new Doctor.Result(
            new[]
            {
                new Preflight.Check(Preflight.Level.Ok, "движок на месте", null, null),
                new Preflight.Check(Preflight.Level.Warning, "прокси в никуда", null, "выключите"),
            },
            Array.Empty<string>(), Array.Empty<string>());

        var verdict = Doctor.Headline(report, "ru");

        Assert.True(report.Healthy);
        Assert.Contains(Strings.T("ru", "doc_warnings", 1), verdict);
        Assert.Equal(verdict, Doctor.Say(report, "ru"));
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

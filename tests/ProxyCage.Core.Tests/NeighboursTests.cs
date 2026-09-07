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

    private static readonly string[] Nobodys = Array.Empty<string>();

    [Fact]
    public void A_working_tunnel_of_another_client_is_never_touched()
    {
        var seen = new List<string>();
        var removable = TunCleanup.Removable(
            new[] { Alien, Ours },
            Adapters(
                (Alien, new TunCleanup.Nic("happ-tun", Up: true, Ours: false)),
                (Ours, new TunCleanup.Nic("tun0", Up: true, Ours: true))),
            new[] { Ours }, seen.Add);

        Assert.Equal(new[] { Ours }, removable.Select(a => a.InstanceId));
        Assert.Contains(seen, m => m.Contains(Alien, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_engine_file_does_not_share_happ_s_process_name()
    {
        if (!Os.IsWindows) return;
        Assert.Equal("ceho-engine.exe", Os.EngineFileName);
        Assert.NotEqual(Os.SingBoxFileName, Os.EngineFileName);
    }

    [Fact]
    public void Matching_the_old_shared_address_is_not_enough_to_remove()
    {
        // Happ и заводской sing-box живут на 172.19.0.1/30. Реальный Look() для этого
        // адреса ставит Ours: false. Живой чужой туннель без записи не снимаем.
        var removable = TunCleanup.Removable(
            new[] { Alien },
            Adapters((Alien, new TunCleanup.Nic("sing-tun", Up: true, Ours: false))),
            Nobodys);

        Assert.Empty(removable);
    }

    [Fact]
    public void Our_unique_address_is_enough_to_remove_without_a_record()
    {
        // Движок упал до записи GUID, адаптер с 172.31.211.1 остался. Без этой проверки
        // следующий старт ловит «файл уже существует» и винит чужой VPN.
        var removable = TunCleanup.Removable(
            new[] { Ours },
            Adapters((Ours, new TunCleanup.Nic("tun0", Up: true, Ours: true))),
            Nobodys);

        Assert.Equal(new[] { Ours }, removable.Select(a => a.InstanceId));
    }

    [Fact]
    public void An_adapter_that_appeared_dead_during_start_is_removed()
    {
        var removable = TunCleanup.Removable(
            new[] { Ours },
            Adapters((Ours, new TunCleanup.Nic("tun0", Up: false, Ours: false))),
            Nobodys,
            beforeStart: Nobodys);

        Assert.Equal(new[] { Ours }, removable.Select(a => a.InstanceId));
    }

    [Fact]
    public void A_live_neighbour_that_appeared_during_start_is_not_claimed()
    {
        var removable = TunCleanup.Removable(
            new[] { Alien },
            Adapters((Alien, new TunCleanup.Nic("happ-tun", Up: true, Ours: false))),
            Nobodys,
            beforeStart: Nobodys);

        Assert.Empty(removable);
    }

    [Fact]
    public void A_dead_adapter_of_a_stranger_stays_where_it_is()
    {
        // Соблазн убрать велик: мёртвый адаптер мешает поднять свой туннель. Но он чужой,
        // и мы в системе гости — жалуемся в осмотре, а руками не трогаем.
        var removable = TunCleanup.Removable(
            new[] { Alien },
            Adapters((Alien, new TunCleanup.Nic("happ-tun", Up: false, Ours: false))),
            Nobodys);

        Assert.Empty(removable);
    }

    [Fact]
    public void Our_own_device_is_removed_even_without_an_adapter()
    {
        // Такое оставляет неудачная попытка запуска движка: адаптера уже нет, а устройство
        // ещё держит GUID. Своим мы его знаем по записи, сделанной при запуске.
        var removable = TunCleanup.Removable(new[] { Ours }, Adapters(), new[] { Ours });

        Assert.Equal(new[] { Ours }, removable.Select(a => a.InstanceId));
    }

    [Fact]
    public void A_ghost_already_there_without_an_adapter_is_removed()
    {
        // Живой случай 1.2.10: устройство без интерфейса уже было до старта, GUID
        // не записан. Первая уборка его пропускала, движок падал, снимали уже после.
        var removable = TunCleanup.Removable(
            new[] { Ours },
            Adapters(),
            Nobodys,
            beforeStart: new[] { Ours });

        Assert.Equal(new[] { Ours }, removable.Select(a => a.InstanceId));
    }

    [Fact]
    public void A_dead_named_neighbour_already_there_is_still_not_touched()
    {
        var removable = TunCleanup.Removable(
            new[] { Alien },
            Adapters((Alien, new TunCleanup.Nic("happ-tun", Up: false, Ours: false))),
            Nobodys,
            beforeStart: new[] { Alien });

        Assert.Empty(removable);
    }

    [Fact]
    public void An_unknown_device_without_an_adapter_is_not_ours_to_remove()
    {
        var removable = TunCleanup.Removable(new[] { Alien }, Adapters(), Nobodys);

        Assert.Empty(removable);
    }

    [Fact]
    public void Devices_that_are_not_tunnels_are_out_of_scope()
    {
        var removable = TunCleanup.Removable(
            new[] { @"PCI\VEN_8086&DEV_51F0&SUBSYS_02448086&REV_01\{0E62885F}" }, Adapters(), Nobodys);

        Assert.Empty(removable);
    }

    [Fact]
    public void Our_adapter_is_recognised_by_its_address()
    {
        var mine = TunCleanup.Mine(
            known: new[] { Alien, Ours },
            recorded: Nobodys, ours: id => id == Ours);

        Assert.Equal(new[] { Ours }, mine);
    }

    [Fact]
    public void A_neighbour_is_not_claimed_just_because_it_appeared()
    {
        // Раньше всё новое за два секунды старта считалось нашим. Если за это время
        // Happ поднимал свой туннель, мы записывали его GUID и потом удаляли.
        var mine = TunCleanup.Mine(
            known: new[] { Alien, Ours },
            recorded: Nobodys, ours: _ => false);

        Assert.Empty(mine);
    }

    [Fact]
    public void A_device_that_disappeared_is_forgotten()
    {
        var mine = TunCleanup.Mine(
            known: new[] { Alien },
            recorded: new[] { Ours }, ours: _ => false);

        Assert.Empty(mine);
    }

    [Fact]
    public void The_shared_sing_box_address_is_not_ours_to_keep()
    {
        Assert.True(CehoConfig.SharesSingBoxTun("172.19.0.1/30"));
        Assert.False(CehoConfig.SharesSingBoxTun(CehoConfig.DefaultTunAddress));
        Assert.NotEqual(CehoConfig.SharedSingBoxTun, CehoConfig.DefaultTunAddress);
    }

    [Fact]
    public void An_old_config_moves_off_the_shared_address()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ceho-tun-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, """{"TunAddress":"172.19.0.1/30","WebPort":8899}""");

        try
        {
            var loaded = CehoConfig.Load(path);
            Assert.Equal(CehoConfig.DefaultTunAddress, loaded.TunAddress);
            Assert.Equal(CehoConfig.DefaultTunAddress, CehoConfig.Load(path).TunAddress);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void A_busy_tunnel_address_is_explained_in_human_words()
    {
        // Раз чужие адаптеры мы больше не сносим, человек должен узнать из панели,
        // что именно ему мешает и что с этим делать.
        var hint = SingBoxProcess.Hint(
            "FATAL create tun interface: Cannot create a file when that file already exists.", "ru");

        Assert.Equal(Strings.T("ru", "engine_tun_busy"), hint);
        Assert.Null(SingBoxProcess.Hint("ERROR connection reset by peer", "ru"));
    }

    [Fact]
    public void Our_interface_is_found_by_its_address()
    {
        // Петля есть на любой машине, поэтому проверка честная и на маке, и на Windows.
        Assert.NotNull(TunCleanup.InterfaceWithAddress("127.0.0.1/8"));
        Assert.Null(TunCleanup.InterfaceWithAddress("203.0.113.7/32"));
    }

    [Fact]
    public void Stuck_adapter_is_visible_in_recent_log()
    {
        var root = Path.Combine(Path.GetTempPath(), "chp-tun-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            Log.Init(root, "test");
            Log.Engine("INFO removed something");
            Log.Engine("FATAL start inbound/tun[tun-in]: configure tun interface: Cannot create a file when that file already exists.");
            Assert.True(TunCleanup.LogShowsStuckAdapter());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
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

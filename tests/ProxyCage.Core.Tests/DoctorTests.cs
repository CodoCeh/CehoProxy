using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class DoctorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ceho-doctor-" + Guid.NewGuid().ToString("N")[..8]);

    public DoctorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string ConfigPath => Path.Combine(_root, "config.json");

    private CehoConfig Config()
    {
        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "app", Folder = Os.IsWindows ? @"C:\Games\App" : "/tmp" } },
            Subscriptions = { new SubscriptionEntry { Name = "sub", Url = "https://example.invalid/sub" } },
        };
        cfg.Save(ConfigPath);
        return cfg;
    }

    private static IReadOnlyList<ProxyNode> Nodes() =>
        SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static DoctorTools Tools(
        Func<IStageReport, Task<IReadOnlyList<ProxyNode>>>? pool = null,
        Func<IStageReport, Task<string>>? rebuild = null) => new()
    {
        Pool = pool ?? (_ => Task.FromResult(Nodes())),
        Rebuild = rebuild,
    };

    [Fact]
    public async Task Missing_rules_are_seen_and_rebuilt_by_the_doctor()
    {
        var cfg = Config();

        var before = await Doctor.CheckAsync(cfg, _root, Tools());
        var rules = before.Checks.Single(c => c.Repair == Repair.Rules);
        Assert.Equal(Preflight.Level.Blocker, rules.Level);

        var built = 0;
        var after = await Doctor.HealAsync(cfg, ConfigPath, _root, Tools(rebuild: _ =>
        {
            built++;
            File.WriteAllText(Path.Combine(_root, "singbox.json"),
                SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg));
            return Task.FromResult("собрал");
        }));

        Assert.Equal(1, built);
        Assert.Contains("собрал", after.Done);
        Assert.DoesNotContain(after.Checks, c => c.Repair == Repair.Rules);
    }

    [Fact]
    public async Task Rules_older_than_settings_are_only_a_warning()
    {
        var cfg = Config();
        var rules = Path.Combine(_root, "singbox.json");
        File.WriteAllText(rules, SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg));
        File.SetLastWriteTimeUtc(rules, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(ConfigPath, DateTime.UtcNow);

        var report = await Doctor.CheckAsync(cfg, _root, Tools());
        var stale = report.Checks.Single(c => c.Repair == Repair.Rules);

        Assert.Equal(Preflight.Level.Warning, stale.Level);
    }

    [Fact]
    public async Task Unreadable_subscriptions_are_a_blocker_the_doctor_does_not_touch()
    {
        var cfg = Config();

        var report = await Doctor.CheckAsync(cfg, _root, Tools(
            pool: _ => throw new InvalidOperationException("подписка не открылась")));

        var dead = report.Checks.Single(c => c.Detail == "подписка не открылась");
        Assert.Equal(Preflight.Level.Blocker, dead.Level);
        Assert.Equal(Repair.None, dead.Repair);
    }

    [Fact]
    public async Task Everything_blocked_by_hand_is_reported_as_an_empty_pool()
    {
        var cfg = Config();
        cfg.BlockedNodes = Nodes().Select(n => n.Key).ToList();
        cfg.Save(ConfigPath);

        var report = await Doctor.CheckAsync(cfg, _root, Tools());

        Assert.Contains(report.Checks, c =>
            c.Level == Preflight.Level.Blocker && c.Title == Strings.T("ru", "doc_pool_empty"));
    }

    [Fact]
    public async Task A_failed_repair_lands_in_what_is_left_for_the_owner()
    {
        var cfg = Config();

        var report = await Doctor.HealAsync(cfg, ConfigPath, _root, Tools(
            rebuild: _ => throw new InvalidOperationException("подписка пустая")));

        Assert.Empty(report.Done);
        Assert.Contains(report.Left, line => line.Contains("подписка пустая"));
    }

    [Fact]
    public async Task Busy_panel_port_is_moved_to_a_free_one_and_saved()
    {
        var cfg = Config();

        using var squatter = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        squatter.Start();
        var busy = ((System.Net.IPEndPoint)squatter.LocalEndpoint).Port;

        cfg.WebPort = busy;
        cfg.Save(ConfigPath);

        var report = await Doctor.HealAsync(cfg, ConfigPath, _root, Tools(
            rebuild: _ => Task.FromResult("собрал")));

        var saved = CehoConfig.Load(ConfigPath);
        Assert.NotEqual(busy, saved.WebPort);
        Assert.Contains(report.Done, line => line.Contains(busy.ToString()));
    }

    [Fact]
    public async Task A_healthy_setup_says_so_in_one_phrase()
    {
        var report = new Doctor.Result(
            new[] { new Preflight.Check(Preflight.Level.Ok, "всё хорошо", null, null) },
            Array.Empty<string>(), Array.Empty<string>());

        Assert.True(report.Healthy);
        Assert.Equal(Strings.T("ru", "doc_all_ok"), Doctor.Say(report, "ru"));
        await Task.CompletedTask;
    }

    [Fact]
    public void Every_repair_the_doctor_knows_is_named_in_both_languages()
    {
        foreach (var repair in Enum.GetValues<Repair>())
        {
            var key = "doc_name_" + string.Concat(repair.ToString()
                .Select((ch, i) => char.IsUpper(ch) && i > 0 ? "_" + char.ToLower(ch) : char.ToLower(ch).ToString()));

            Assert.NotEqual(key, Strings.T("ru", key));
            Assert.NotEqual(key, Strings.T("en", key));
        }
    }
}

namespace ProxyCage.Core.Tests;

public class StartupPreflightTests
{
    private static CehoConfig Config() => new()
    {
        Language = "en",
        Apps = new() { new AppEntry { Folder = "/apps/example" } },
        Subscriptions = new() { new SubscriptionEntry { Name = "test", Url = "https://example.test/sub" } },
    };

    [Fact]
    public void Valid_enabled_configuration_has_no_cheap_blocker() =>
        Assert.Null(StartupPreflight.ConfigurationError(Config(), _ => false));

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(65536)]
    public void Invalid_port_prevents_expensive_start(int port)
    {
        var config = Config(); config.ClashApiPort = port;
        Assert.Contains("ports", StartupPreflight.ConfigurationError(config, _ => false)!);
    }

    [Fact]
    public void Colliding_api_and_proxy_ports_are_rejected()
    {
        var config = Config(); config.ClashApiPort = config.MixedPort;
        Assert.Contains("different", StartupPreflight.ConfigurationError(config, _ => false)!);
    }

    [Fact]
    public void Disabled_subscriptions_and_empty_app_paths_are_genuine_blockers()
    {
        var config = Config(); config.Subscriptions[0].Enabled = false;
        Assert.NotNull(StartupPreflight.ConfigurationError(config, _ => false));
        config = Config(); config.Apps[0].Folder = " ";
        Assert.NotNull(StartupPreflight.ConfigurationError(config, _ => false));
    }

    [Theory]
    [InlineData("")] [InlineData("not a subscription")] [InlineData("file:///missing")] [InlineData("javascript:alert(1)")]
    public void Unsupported_sources_do_not_trigger_network_work(string source)
    {
        var config = Config(); config.Subscriptions[0].Url = source;
        Assert.NotNull(StartupPreflight.ConfigurationError(config, _ => false));
    }

    [Fact]
    public void Local_file_check_is_injected_without_reading_real_files()
    {
        Assert.True(StartupPreflight.IsUsableSource("/private/sub.txt", path => path == "/private/sub.txt"));
        Assert.False(StartupPreflight.IsUsableSource("/private/missing.txt", _ => false));
    }
}

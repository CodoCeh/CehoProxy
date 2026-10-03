using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class CehoConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-config-" + Guid.NewGuid().ToString("N"));

    public CehoConfigTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Saving_subscription_status_preserves_the_rules_timestamp()
    {
        var path = Path.Combine(_root, "config.json");
        var cfg = new CehoConfig();
        cfg.Save(path);
        var expected = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(path, expected);

        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "sub", Url = "https://example.test/sub", LastCheckOk = true });
        cfg.SaveSubscriptionStatus(path);

        Assert.Equal(expected, File.GetLastWriteTimeUtc(path));
        Assert.True(CehoConfig.Load(path).Subscriptions.Single().LastCheckOk);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Apps\":null}")]
    [InlineData("{\"Apps\":[null]}")]
    [InlineData("{\"Subscriptions\":null}")]
    [InlineData("{\"Subscriptions\":[null]}")]
    [InlineData("{\"NaiveProxy\":null}")]
    [InlineData("{\"PreferredCountries\":null}")]
    [InlineData("{\"TunAddress\":\"172.31.211.1/99\"}")]
    [InlineData("{\"MixedPort\":65536}")]
    [InlineData("{\"CheckUrl\":\"file:///private\"}")]
    public void Invalid_existing_configuration_is_not_silently_reset_or_migrated(string invalid)
    {
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, invalid);
        Assert.Throws<InvalidDataException>(() => CehoConfig.Load(path));
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void Invalid_save_preserves_previous_bytes_and_timestamp()
    {
        var path = Path.Combine(_root, "config.json");
        var cfg = new CehoConfig();
        cfg.Save(path);
        var before = File.ReadAllBytes(path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        cfg.Apps.Add(null!);
        Assert.Throws<InvalidDataException>(() => cfg.Save(path));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("drive:stream")]
    [InlineData("bad\0name")]
    public void Subscription_names_cannot_escape_or_alias_cache_paths(string name)
    {
        var cfg = new CehoConfig { Subscriptions = { new SubscriptionEntry { Name = name, Url = "https://example.test/sub" } } };
        Assert.Throws<InvalidDataException>(() => cfg.Save(Path.Combine(_root, "config.json")));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void Legacy_optional_site_and_node_lists_are_still_normalized()
    {
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, "{\"DirectSites\":null,\"SiteCountries\":null,\"Apps\":[{\"AllowedNodes\":null}]}");
        var cfg = CehoConfig.Load(path);
        Assert.Empty(cfg.DirectSites);
        Assert.Empty(cfg.SiteCountries);
        Assert.Empty(Assert.Single(cfg.Apps).AllowedNodes);
    }

    [Fact]
    public void Legacy_proxy_remark_cannot_create_an_unsafe_cache_name_during_migration()
    {
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, "{\"NaiveProxy\":{\"Enabled\":true,\"Server\":\"example.test\",\"Username\":\"user\",\"Password\":\"test-password\",\"Remark\":\"../foreign\"}}");
        var cfg = CehoConfig.Load(path);
        cfg.Validate();
        Assert.Equal("NaiveProxy", Assert.Single(cfg.Subscriptions).Name);
        Assert.Contains("foreign", Assert.Single(cfg.Subscriptions).Url);
        Assert.Equal("NaiveProxy", Assert.Single(CehoConfig.Load(path).Subscriptions).Name);
    }
}

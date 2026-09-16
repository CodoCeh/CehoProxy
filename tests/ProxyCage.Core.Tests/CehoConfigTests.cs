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
}

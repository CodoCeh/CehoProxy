using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class ConnPingTests
{
    [Fact]
    public void Summary_formats_both_ok()
    {
        var report = new ConnPingReport(
            new PingProbeResult("https://test", 4, 4, 100.0, 10, 20, 15, null, Array.Empty<PingAttempt>()),
            new PingProbeResult("https://test", 4, 4, 100.0, 70, 90, 80, null, Array.Empty<PingAttempt>()),
            DateTime.UtcNow);

        var summary = ConnPing.Summary(report, "ru");
        Assert.Contains("100%", summary);
        Assert.Contains("15", summary);
        Assert.Contains("80", summary);
    }

    [Fact]
    public void Summary_formats_proxy_failed()
    {
        var report = new ConnPingReport(
            new PingProbeResult("https://test", 4, 4, 100.0, 10, 20, 15, null, Array.Empty<PingAttempt>()),
            new PingProbeResult("https://test", 4, 0, 0.0, 0, 0, 0, "таймаут", Array.Empty<PingAttempt>()),
            DateTime.UtcNow);

        var summary = ConnPing.Summary(report, "ru");
        Assert.Contains("таймаут", summary);
        Assert.Contains("прокси не отвечает", summary);
    }

    [Fact]
    public void Summary_formats_direct_failed()
    {
        var report = new ConnPingReport(
            new PingProbeResult("https://test", 4, 0, 0.0, 0, 0, 0, "соединение отвергнуто", Array.Empty<PingAttempt>()),
            new PingProbeResult("https://test", 4, 0, 0.0, 0, 0, 0, "соединение отвергнуто", Array.Empty<PingAttempt>()),
            DateTime.UtcNow);

        var summary = ConnPing.Summary(report, "ru");
        Assert.Contains("Прямой интернет недоступен", summary);
    }
}

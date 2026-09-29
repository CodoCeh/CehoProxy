using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class ClashLatencyTests
{
    [Fact]
    public void Reads_the_last_delay_from_history_as_sing_box_reports_it()
    {
        const string json = """
            {"proxies":{
              "GLOBAL":{"type":"Fallback","history":[]},
              "n001":{"type":"Naive","history":[{"time":"2026-09-28T23:20:00+03:00","delay":610},{"time":"2026-09-28T23:22:23+03:00","delay":497}]},
              "n002":{"type":"Hysteria2","history":[{"time":"2026-09-28T23:22:23+03:00","delay":0}]},
              "n003":{"type":"Vless","delay":120},
              "direct":{"type":"Direct","history":[]}
            }}
            """;

        var delays = ClashLatency.Parse(json);

        Assert.Equal(497, delays["n001"]);
        Assert.Equal(120, delays["n003"]);
        Assert.False(delays.ContainsKey("n002"));
        Assert.False(delays.ContainsKey("direct"));
    }
}

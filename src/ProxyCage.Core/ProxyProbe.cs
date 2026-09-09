namespace ProxyCage.Core;

/// <summary>
/// Exit checks through CehoProxy mixed inbound (HTTP/SOCKS5 on 127.0.0.1:MixedPort).
/// </summary>
public static class ProxyProbe
{
    public sealed record MixedTestResult(
        string? HttpIp,
        string? SocksIp,
        bool EngineReachable,
        bool HttpOk,
        bool SocksOk)
    {
        public bool Ok => HttpOk && SocksOk;
    }

    public static MixedTestResult TestMixed(int mixedPort, string? expectedIp = null)
    {
        var curl = Os.ResolveCurl();
        if (curl is null)
            return new MixedTestResult(null, null, false, false, false);

        var httpIp = CurlIp(curl, $"--proxy http://127.0.0.1:{mixedPort}");
        var socksIp = CurlIp(curl, $"--proxy socks5h://127.0.0.1:{mixedPort}");

        var httpOk = IpMatches(httpIp, expectedIp);
        var socksOk = IpMatches(socksIp, expectedIp);

        return new MixedTestResult(
            httpIp,
            socksIp,
            EngineReachable: httpIp is not null || socksIp is not null,
            httpOk,
            socksOk);
    }

    public static string FormatResult(MixedTestResult r, string lang, string? expectedIp = null)
    {
        if (!r.EngineReachable)
            return Strings.T(lang, "proxy_test_no_engine");

        var lines = new List<string>
        {
            Strings.T(lang, "proxy_test_http", r.HttpIp ?? "—"),
            Strings.T(lang, "proxy_test_socks", r.SocksIp ?? "—"),
        };

        if (!string.IsNullOrWhiteSpace(expectedIp))
        {
            lines.Add(r.Ok
                ? Strings.T(lang, "proxy_test_expected_ok", expectedIp)
                : Strings.T(lang, "proxy_test_expected_fail", expectedIp));
        }
        else if (r.Ok)
            lines.Add(Strings.T(lang, "proxy_test_ok"));
        else
            lines.Add(Strings.T(lang, "proxy_test_partial"));

        return string.Join("\n", lines);
    }

    private static string? CurlIp(string curl, string proxyArgs)
    {
        var (code, output) = Os.Run(curl,
            $"-s --max-time 20 {proxyArgs} https://api.ipify.org", 25000);
        if (code != 0) return null;
        var ip = output.Trim();
        return ip.Length is >= 7 and <= 45 ? ip : null;
    }

    private static bool IpMatches(string? actual, string? expected) =>
        actual is { Length: > 0 }
        && (expected is null || string.Equals(actual.Trim(), expected.Trim(), StringComparison.Ordinal));
}

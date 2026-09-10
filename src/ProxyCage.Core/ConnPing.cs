using System.Diagnostics;
using System.Net;

namespace ProxyCage.Core;

public sealed record PingAttempt(int Index, bool Success, long LatencyMs, string? Error);

public sealed record PingProbeResult(
    string Target,
    int TotalAttempts,
    int SuccessCount,
    double SuccessPercent,
    long MinMs,
    long MaxMs,
    long AvgMs,
    string? LastError,
    IReadOnlyList<PingAttempt> Attempts)
{
    public bool Ok => SuccessCount > 0;
}

public sealed record ConnPingReport(
    PingProbeResult Direct,
    PingProbeResult Proxy,
    DateTime CheckedAtUtc);

/// <summary>
/// Диагностика доступности интернета в 2 этапа:
/// 1 этап — напрямую (без прокси)
/// 2 этап — через локальный прокси CehoProxy (ноду)
/// </summary>
public static class ConnPing
{
    public const int DefaultAttempts = 4;
    public const string DefaultTarget = "https://www.gstatic.com/generate_204";

    public static async Task<ConnPingReport> RunAsync(
        CehoConfig cfg,
        Action<string, int>? onProgress = null,
        int attempts = DefaultAttempts,
        CancellationToken ct = default)
    {
        var target = string.IsNullOrWhiteSpace(cfg.CheckUrl) ? DefaultTarget : cfg.CheckUrl;

        // Этап 1: Прямое подключение (без прокси)
        onProgress?.Invoke(Strings.T(cfg.Language, "ping_stage_direct"), 10);
        var direct = await ProbeAsync(target, null, attempts, (cur, tot) =>
        {
            var pct = 10 + (int)(35.0 * cur / tot);
            onProgress?.Invoke(Strings.T(cfg.Language, "ping_stage_direct_n", cur, tot), pct);
        }, ct);

        // Этап 2: Через прокси-ноду (локальный порт mixed-in)
        onProgress?.Invoke(Strings.T(cfg.Language, "ping_stage_proxy"), 50);
        var proxy = await ProbeAsync(target, $"http://127.0.0.1:{cfg.MixedPort}", attempts, (cur, tot) =>
        {
            var pct = 50 + (int)(45.0 * cur / tot);
            onProgress?.Invoke(Strings.T(cfg.Language, "ping_stage_proxy_n", cur, tot), pct);
        }, ct);

        onProgress?.Invoke(Strings.T(cfg.Language, "stage_done"), 100);
        return new ConnPingReport(direct, proxy, DateTime.UtcNow);
    }

    public static async Task<PingProbeResult> ProbeAsync(
        string targetUrl,
        string? proxyUrl,
        int attempts = DefaultAttempts,
        Action<int, int>? onStep = null,
        CancellationToken ct = default)
    {
        var attemptsList = new List<PingAttempt>();
        var latencies = new List<long>();
        string? lastError = null;

        for (var i = 1; i <= attempts; i++)
        {
            onStep?.Invoke(i, attempts);
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(3.5),
                    PooledConnectionLifetime = TimeSpan.Zero,
                };
                if (!string.IsNullOrEmpty(proxyUrl))
                {
                    handler.Proxy = new WebProxy(proxyUrl);
                    handler.UseProxy = true;
                }
                else
                {
                    handler.UseProxy = false;
                }

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(4),
                };

                using var req = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", "CehoProxy/1.0");

                var sw = Stopwatch.StartNew();
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                sw.Stop();

                var elapsed = Math.Max(1, sw.ElapsedMilliseconds);
                var isOk = (int)resp.StatusCode < 400 || resp.StatusCode == HttpStatusCode.NoContent;

                if (isOk)
                {
                    latencies.Add(elapsed);
                    attemptsList.Add(new PingAttempt(i, true, elapsed, null));
                }
                else
                {
                    var err = $"HTTP {(int)resp.StatusCode}";
                    lastError = err;
                    attemptsList.Add(new PingAttempt(i, false, elapsed, err));
                }
            }
            catch (Exception ex)
            {
                var err = SimplifyError(ex);
                lastError = err;
                attemptsList.Add(new PingAttempt(i, false, 0, err));
            }

            if (i < attempts)
            {
                try { await Task.Delay(150, ct); } catch { }
            }
        }

        var successCount = attemptsList.Count(a => a.Success);
        var successPercent = attempts > 0 ? Math.Round((successCount * 100.0) / attempts, 1) : 0.0;
        var minMs = latencies.Count > 0 ? latencies.Min() : 0;
        var maxMs = latencies.Count > 0 ? latencies.Max() : 0;
        var avgMs = latencies.Count > 0 ? (long)Math.Round(latencies.Average()) : 0;

        return new PingProbeResult(
            targetUrl,
            attempts,
            successCount,
            successPercent,
            minMs,
            maxMs,
            avgMs,
            lastError,
            attemptsList);
    }

    public static string Summary(ConnPingReport r, string lang)
    {
        if (r.Direct.Ok && r.Proxy.Ok)
        {
            return Strings.T(lang, "ping_summary_both_ok",
                r.Direct.SuccessPercent, r.Direct.AvgMs,
                r.Proxy.SuccessPercent, r.Proxy.AvgMs);
        }

        if (r.Direct.Ok && !r.Proxy.Ok)
        {
            return Strings.T(lang, "ping_summary_proxy_fail",
                r.Direct.SuccessPercent, r.Direct.AvgMs,
                r.Proxy.LastError ?? Strings.T(lang, "stage_failed"));
        }

        if (!r.Direct.Ok)
        {
            if (TunCleanup.LogShowsStuckAdapter())
                return Strings.T(lang, "ping_summary_tun_hijack",
                    r.Direct.LastError ?? Strings.T(lang, "stage_failed"));
            return Strings.T(lang, "ping_summary_direct_fail",
                r.Direct.LastError ?? Strings.T(lang, "stage_failed"));
        }

        return Strings.T(lang, "ping_summary_proxy_fail",
            r.Direct.SuccessPercent, r.Direct.AvgMs,
            r.Proxy.LastError ?? Strings.T(lang, "stage_failed"));
    }

    private static string SimplifyError(Exception ex)
    {
        if (ex is OperationCanceledException or TimeoutException)
            return "таймаут";

        if (ex.InnerException != null)
            return SimplifyError(ex.InnerException);

        var msg = ex.Message;
        if (msg.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("отверг", StringComparison.OrdinalIgnoreCase))
            return "соединение отвергнуто";

        if (msg.Contains("deadline exceeded", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "таймаут";

        if (msg.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("неизвестен", StringComparison.OrdinalIgnoreCase))
            return "DNS не разрешил хост";

        return msg.Length > 60 ? msg[..57] + "…" : msg;
    }
}

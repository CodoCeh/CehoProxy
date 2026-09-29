using System.Net.Http;
using System.Text.Json;

namespace ProxyCage.Core;

/// <summary>
/// Задержки urltest из Clash API sing-box. Пока защита включена, это единственный
/// честный источник чисел для панели — прямой TCP-замер через TUN бессмысленен.
/// </summary>
public static class ClashLatency
{
    public static async Task<IReadOnlyDictionary<string, int>> ReadDelaysAsync(
        int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/proxies", cancellationToken);
            if (!response.IsSuccessStatusCode) return Empty;

            return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch
        {
            return Empty;
        }
    }

    internal static IReadOnlyDictionary<string, int> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("proxies", out var proxies)) return Empty;

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in proxies.EnumerateObject())
        {
            if (Delay(item.Value) is { } delay) result[item.Name] = delay;
        }
        return result;
    }

    private static int? Delay(JsonElement proxy)
    {
        if (proxy.TryGetProperty("delay", out var direct) && direct.TryGetInt32(out var d) && d > 0) return d;
        if (!proxy.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array) return null;
        var last = history.EnumerateArray().LastOrDefault();
        return last.ValueKind == JsonValueKind.Object
               && last.TryGetProperty("delay", out var h) && h.TryGetInt32(out var ms) && ms > 0
            ? ms
            : null;
    }

    private static readonly IReadOnlyDictionary<string, int> Empty =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

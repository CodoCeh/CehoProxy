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

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("proxies", out var proxies)) return Empty;

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in proxies.EnumerateObject())
            {
                if (!item.Value.TryGetProperty("delay", out var delayEl)) continue;
                if (delayEl.TryGetInt32(out var delay) && delay > 0)
                    result[item.Name] = delay;
            }

            return result;
        }
        catch
        {
            return Empty;
        }
    }

    private static readonly IReadOnlyDictionary<string, int> Empty =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

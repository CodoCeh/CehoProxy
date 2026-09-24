using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProxyCage.Core;

public interface INodeExitIpProbe
{
    Task<IPAddress?> ProbeAsync(ProxyNode node, CancellationToken cancellationToken);
}

public interface INodeCountryLookup
{
    string? LookupCountry(IPAddress address);
}

public sealed class NodeCountryService
{
    private sealed record CachedCountry(string Code, DateTimeOffset VerifiedAtUtc, string DatabaseVersion);

    private readonly INodeExitIpProbe _exitProbe;
    private readonly INodeCountryLookup _lookup;
    private readonly string _cachePath;
    private readonly string _databaseVersion;
    private readonly TimeSpan _cacheLifetime;
    private readonly int _maxConcurrency;
    private readonly ConcurrentDictionary<string, CachedCountry> _cache = new(StringComparer.Ordinal);

    public NodeCountryService(
        INodeExitIpProbe exitProbe,
        INodeCountryLookup lookup,
        string cachePath,
        string databaseVersion,
        TimeSpan? cacheLifetime = null,
        int maxConcurrency = 3)
    {
        _exitProbe = exitProbe;
        _lookup = lookup;
        _cachePath = cachePath;
        _databaseVersion = databaseVersion;
        _cacheLifetime = cacheLifetime ?? TimeSpan.FromDays(1);
        _maxConcurrency = Math.Clamp(maxConcurrency, 1, 8);
        LoadCache();
    }

    public string DatabaseVersion => _databaseVersion;

    public async Task ApplyCachedAsync(IReadOnlyList<ProxyNode> nodes, CancellationToken cancellationToken = default)
    {
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.IsMeta) continue;
            node.CountryCode = null;
            node.CountryName = null;
            if (_cache.TryGetValue(Fingerprint(node), out var cached)
                && cached.DatabaseVersion == _databaseVersion
                && DateTimeOffset.UtcNow - cached.VerifiedAtUtc <= _cacheLifetime)
                SetCountry(node, cached.Code);
        }
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<NodeProbe.CountryRow>> RefreshAsync(
        IReadOnlyList<ProxyNode> nodes,
        int probeTimeoutMs = 15000,
        Action<int, int>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceProbe = false)
    {
        var real = nodes.Where(n => !n.IsMeta).ToList();
        var semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        var done = 0;
        await Task.WhenAll(real.Select(async node =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                node.CountryCode = null;
                node.CountryName = null;
                var key = Fingerprint(node);
                if (!forceProbe && _cache.TryGetValue(key, out var cached)
                    && cached.DatabaseVersion == _databaseVersion
                    && DateTimeOffset.UtcNow - cached.VerifiedAtUtc <= CacheLifetime(cached))
                {
                    if (IsCountryCode(cached.Code)) SetCountry(node, cached.Code);
                    return;
                }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(probeTimeoutMs);
                try
                {
                    var ip = await _exitProbe.ProbeAsync(node, timeout.Token);
                    if (ip is not null && !IPAddress.IsLoopback(ip) && !ip.IsIPv6LinkLocal)
                    {
                        var code = _lookup.LookupCountry(ip);
                        if (IsCountryCode(code))
                        {
                            SetCountry(node, code!);
                            _cache[key] = new CachedCountry(code!, DateTimeOffset.UtcNow, _databaseVersion);
                        }
                        else _cache[key] = UnknownCache();
                    }
                    else _cache[key] = UnknownCache();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { _cache[key] = UnknownCache(); }
                catch (OperationCanceledException) { throw; }
                catch { _cache[key] = UnknownCache(); }
            }
            finally
            {
                semaphore.Release();
                progress?.Invoke(Interlocked.Increment(ref done), real.Count);
            }
        }));
        await SaveCacheAsync(cancellationToken);
        return Group(real);
    }

    public static IReadOnlyList<NodeProbe.CountryRow> Group(IReadOnlyList<ProxyNode> nodes)
    {
        var measured = nodes.Where(n => !n.IsMeta).Select(n => new NodeProbe.Measured(n, null)).ToList();
        return measured.GroupBy(m => m.Node.CountryCode ?? CountryResolver.Unknown)
            .Select(g => new NodeProbe.CountryRow(g.Key, g.First().Node.CountryName, g.Count(), 0, null, g.ToList()))
            .OrderBy(r => r.Code == CountryResolver.Unknown ? 1 : 0)
            .ThenBy(r => r.Name ?? r.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void SetCountry(ProxyNode node, string code)
    {
        node.CountryCode = code;
        node.CountryName = CountryResolver.DisplayName(code);
    }

    private static bool IsCountryCode(string? value) =>
        value is { Length: 2 } && value.All(char.IsAsciiLetter) && value != CountryResolver.Unknown;

    private CachedCountry UnknownCache() => new(CountryResolver.Unknown, DateTimeOffset.UtcNow, _databaseVersion);

    private TimeSpan CacheLifetime(CachedCountry cached) =>
        IsCountryCode(cached.Code) ? _cacheLifetime : TimeSpan.FromMinutes(10);

    private static string Fingerprint(ProxyNode node)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(node.Key));
        return Convert.ToHexString(bytes);
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, CachedCountry>>(File.ReadAllText(_cachePath));
            if (data is null) return;
            foreach (var pair in data) _cache.TryAdd(pair.Key, pair.Value);
        }
        catch { }
    }

    private async Task SaveCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = _cachePath + ".tmp";
            var current = _cache.Where(p => DateTimeOffset.UtcNow - p.Value.VerifiedAtUtc <= CacheLifetime(p.Value))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(current), cancellationToken);
            File.Move(temp, _cachePath, overwrite: true);
        }
        catch { }
    }
}

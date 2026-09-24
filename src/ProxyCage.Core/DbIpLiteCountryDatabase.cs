using System.IO.Compression;
using System.Net;
using System.Text.Json.Serialization;
using MaxMind.Db;

namespace ProxyCage.Core;

public sealed class DbIpLiteCountryDatabase : INodeCountryLookup, IDisposable
{
    public const string AttributionUrl = "https://db-ip.com";
    private const string DownloadRoot = "https://download.db-ip.com/free";
    private const string BundledVersion = "2026-09";
    private const string BundledResource = "ProxyCage.Core.Assets.dbip-country-lite-2026-09.mmdb.gz";
    private readonly string _root;
    private readonly string _path;
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly string _downloadRoot;
    private readonly TimeSpan _downloadTimeout;
    private readonly bool _useBundled;
    private readonly object _readerLock = new();
    private Reader? _reader;

    public DbIpLiteCountryDatabase(string root)
        : this(root, () => DirectHttp.CreateClient(TimeSpan.FromSeconds(15)), DownloadRoot,
            TimeSpan.FromSeconds(15), useBundled: true)
    {
    }

    internal DbIpLiteCountryDatabase(
        string root, Func<HttpClient> httpClientFactory, string downloadRoot, TimeSpan downloadTimeout,
        bool useBundled = false)
    {
        _root = root;
        _path = Path.Combine(root, "dbip-country-lite.mmdb");
        _httpClientFactory = httpClientFactory;
        _downloadRoot = downloadRoot.TrimEnd('/');
        _downloadTimeout = downloadTimeout;
        _useBundled = useBundled;
        TryOpenExisting();
    }

    public bool IsAvailable { get { lock (_readerLock) return _reader is not null; } }
    public string Version { get; private set; } = "unavailable";

    public string? LookupCountry(IPAddress address)
    {
        lock (_readerLock)
        {
            if (_reader is null) return null;
            try { return _reader.Find<CountryRecord>(address)?.Country?.IsoCode; }
            catch { return null; }
        }
    }

    public async Task<bool> EnsureCurrentAsync(CancellationToken cancellationToken = default)
    {
        var month = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        if (IsAvailable && Version == month) return true;
        Directory.CreateDirectory(_root);
        if (_useBundled && !IsAvailable)
        {
            await InstallBundledAsync(cancellationToken);
            if (IsAvailable && Version == month) return true;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_downloadTimeout);
        for (var offset = 0; offset <= 2; offset++)
        {
            var release = DateTimeOffset.UtcNow.AddMonths(-offset).ToString("yyyy-MM");
            var url = $"{_downloadRoot}/dbip-country-lite-{release}.mmdb.gz";
            var compressed = _path + ".gz.tmp";
            var unpacked = _path + ".tmp";
            try
            {
                using var http = _httpClientFactory();
                await using (var source = await http.GetStreamAsync(url, deadline.Token))
                await using (var destination = File.Create(compressed))
                    await source.CopyToAsync(destination, deadline.Token);
                await using (var source = File.OpenRead(compressed))
                await using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                await using (var destination = File.Create(unpacked))
                    await gzip.CopyToAsync(destination, deadline.Token);
                using (var verify = new Reader(unpacked, FileAccessMode.Memory))
                    _ = verify.Find<CountryRecord>(IPAddress.Loopback);

                File.Move(unpacked, _path, overwrite: true);
                var replacement = new Reader(_path);
                lock (_readerLock)
                {
                    _reader?.Dispose();
                    _reader = replacement;
                    Version = release;
                }
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { return IsAvailable; }
            catch { }
            finally
            {
                TryDelete(compressed);
                TryDelete(unpacked);
            }
        }
        return IsAvailable;
    }

    internal async Task InstallBundledAsync(CancellationToken cancellationToken = default)
    {
        var unpacked = _path + ".bundled.tmp";
        try
        {
            Directory.CreateDirectory(_root);
            await using var compressed = typeof(DbIpLiteCountryDatabase).Assembly
                .GetManifestResourceStream(BundledResource)
                ?? throw new InvalidDataException("Bundled country database is missing.");
            await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            await using (var destination = File.Create(unpacked))
                await gzip.CopyToAsync(destination, cancellationToken);
            using (var verify = new Reader(unpacked, FileAccessMode.Memory))
                _ = verify.Find<CountryRecord>(IPAddress.Loopback);
            File.Move(unpacked, _path, overwrite: true);
            File.SetLastWriteTimeUtc(_path, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
            var replacement = new Reader(_path);
            lock (_readerLock)
            {
                _reader?.Dispose();
                _reader = replacement;
                Version = BundledVersion;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Log.Warn($"встроенная геобаза стран недоступна: {error.Message}"); }
        finally { TryDelete(unpacked); }
    }

    private void TryOpenExisting()
    {
        try
        {
            if (!File.Exists(_path)) return;
            _reader = new Reader(_path);
            Version = File.GetLastWriteTimeUtc(_path).ToString("yyyy-MM");
        }
        catch { _reader = null; }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    public void Dispose() { lock (_readerLock) { _reader?.Dispose(); _reader = null; } }

    public sealed class CountryRecord
    {
        [MapKey("country")]
        public Country? Country { get; init; }
    }

    public sealed class Country
    {
        [MapKey("iso_code")]
        public string? IsoCode { get; init; }
    }
}

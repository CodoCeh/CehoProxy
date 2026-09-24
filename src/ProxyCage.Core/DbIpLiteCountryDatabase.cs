using System.IO.Compression;
using System.Net;
using System.Text.Json.Serialization;
using MaxMind.Db;

namespace ProxyCage.Core;

public sealed class DbIpLiteCountryDatabase : INodeCountryLookup, IDisposable
{
    public const string AttributionUrl = "https://db-ip.com";
    private const string DownloadRoot = "https://download.db-ip.com/free";
    private readonly string _root;
    private readonly string _path;
    private readonly object _readerLock = new();
    private Reader? _reader;

    public DbIpLiteCountryDatabase(string root)
    {
        _root = root;
        _path = Path.Combine(root, "dbip-country-lite.mmdb");
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
        for (var offset = 0; offset <= 2; offset++)
        {
            var release = DateTimeOffset.UtcNow.AddMonths(-offset).ToString("yyyy-MM");
            var url = $"{DownloadRoot}/dbip-country-lite-{release}.mmdb.gz";
            var compressed = _path + ".gz.tmp";
            var unpacked = _path + ".tmp";
            try
            {
                using var http = DirectHttp.CreateClient(TimeSpan.FromSeconds(15));
                await using (var source = await http.GetStreamAsync(url, cancellationToken))
                await using (var destination = File.Create(compressed))
                    await source.CopyToAsync(destination, cancellationToken);
                await using (var source = File.OpenRead(compressed))
                await using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                await using (var destination = File.Create(unpacked))
                    await gzip.CopyToAsync(destination, cancellationToken);
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
            catch { }
            finally
            {
                TryDelete(compressed);
                TryDelete(unpacked);
            }
        }
        return IsAvailable;
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

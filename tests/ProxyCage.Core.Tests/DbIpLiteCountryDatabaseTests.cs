using System.Net;

namespace ProxyCage.Core.Tests;

[CollectionDefinition("Country database download", DisableParallelization = true)]
public sealed class CountryDatabaseDownloadCollection { }

[Collection("Country database download")]
public sealed class DbIpLiteCountryDatabaseTests
{
    [Fact]
    public async Task Bundled_database_is_readable_and_survives_reopening()
    {
        var root = Path.Combine(Path.GetTempPath(), "cehoproxy-geo-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var database = new DbIpLiteCountryDatabase(root))
            {
                await database.InstallBundledAsync();
                Assert.True(database.IsAvailable);
                Assert.Equal("2026-09", database.Version);
                Assert.Equal("US", database.LookupCountry(IPAddress.Parse("8.8.8.8")));
            }
            using var reopened = new DbIpLiteCountryDatabase(root);
            Assert.True(reopened.IsAvailable);
            Assert.Equal("2026-09", reopened.Version);
            Assert.Equal("US", reopened.LookupCountry(IPAddress.Parse("8.8.8.8")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureCurrentAsync_times_out_if_response_body_stalls_after_headers()
    {
        var root = Path.Combine(Path.GetTempPath(), "cehoproxy-geo-test-" + Guid.NewGuid().ToString("N"));
        var handler = new StallingHandler();
        using var database = new DbIpLiteCountryDatabase(
            root,
            () => new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            "https://geo.test",
            TimeSpan.FromMilliseconds(400));
        using var callerDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var available = await database.EnsureCurrentAsync(callerDeadline.Token)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(available);
            Assert.Equal(1, handler.RequestCount);
            Assert.True(handler.Stream?.ReadCount > 0);
            Assert.InRange(stopwatch.ElapsedMilliseconds, 300, 2500);
            Assert.False(File.Exists(Path.Combine(root, "dbip-country-lite.mmdb.gz.tmp")));
            Assert.False(File.Exists(Path.Combine(root, "dbip-country-lite.mmdb.tmp")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StallingHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);
        public StallingStream? Stream { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            Stream = new StallingStream();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(Stream),
            });
        }
    }

    private sealed class StallingStream : Stream
    {
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

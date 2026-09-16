namespace ProxyCage.Core.Tests;

public sealed class UpdaterIntegrityTests
{
    [Fact]
    public async Task Truncated_release_preserves_installed_binary()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-update-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "client");
            await File.WriteAllTextAsync(target, "original");
            using var client = new HttpClient(new ContentHandler());
            var release = new Updater.Release("9.0.0", "https://example.test/client", 4 * 1024 * 1024, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Updater.InstallAsync(release, target, client));
            Assert.Equal("original", await File.ReadAllTextAsync(target));
            Assert.False(File.Exists(target + ".new"));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[2 * 1024 * 1024]) });
    }
}

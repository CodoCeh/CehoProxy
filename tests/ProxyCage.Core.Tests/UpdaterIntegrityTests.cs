namespace ProxyCage.Core.Tests;

public sealed class UpdaterIntegrityTests
{
    [Fact]
    public void Failed_final_move_restores_installed_binary()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-rollback-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "client");
            File.WriteAllText(target, "original");
            Assert.Throws<FileNotFoundException>(() => Updater.ReplaceDownloadedFile(target + ".missing", target, target + ".old"));
            Assert.Equal("original", File.ReadAllText(target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Deferred_download_does_not_replace_running_binary()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-stage-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "client");
            await File.WriteAllTextAsync(target, "original");
            using var client = new HttpClient(new ContentHandler());
            var staged = await Updater.DownloadAsync(
                new("9.0.0", "https://example.test/client", 2 * 1024 * 1024, null),
                target, client);

            Assert.Equal(target + ".new", staged);
            Assert.Equal("original", await File.ReadAllTextAsync(target));
            Assert.Equal(2 * 1024 * 1024, new FileInfo(staged).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Valid_update_in_a_path_with_spaces_is_executable()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "ceho update " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "client");
            await File.WriteAllTextAsync(target, "original");
            using var client = new HttpClient(new ContentHandler());
            await Updater.InstallAsync(new("9.0.0", "https://example.test/client", 2 * 1024 * 1024, null), target, client);
            Assert.True(File.GetUnixFileMode(target).HasFlag(UnixFileMode.UserExecute));
            Assert.Equal("original", await File.ReadAllTextAsync(target + ".old"));
        }
        finally { Directory.Delete(root, true); }
    }

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

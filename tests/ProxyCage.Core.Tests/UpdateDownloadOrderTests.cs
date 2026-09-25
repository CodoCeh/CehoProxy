using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class UpdateDownloadOrderTests
{
    [Fact]
    public async Task Failed_tunnel_shutdown_deletes_staged_update()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-update-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var staged = Path.Combine(root, "cehoproxy.new");
            var result = await Updater.DownloadThenPrepareForUpdateAsync(
                async () =>
                {
                    await File.WriteAllTextAsync(staged, "new binary");
                    return staged;
                },
                () => Task.FromResult(new TunnelShutdown.Result(false, "upd_need_reboot", null)));

            Assert.False(result.Shutdown.Ok);
            Assert.False(File.Exists(staged));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Download_finishes_while_service_is_up_before_tunnel_shutdown()
    {
        var serviceIsUp = true;
        var downloadCompleted = false;

        var (downloaded, shutdown) = await Updater.DownloadThenPrepareForUpdateAsync(
            async () =>
            {
                Assert.True(serviceIsUp, "The service must stay up during download.");
                await Task.Yield();
                downloadCompleted = true;
                return "staged.exe.new";
            },
            () => Task.Run(() =>
            {
                Assert.True(downloadCompleted, "Download must finish before tunnel shutdown.");
                serviceIsUp = false;
                return new TunnelShutdown.Result(true, null, null);
            }));

        Assert.Equal("staged.exe.new", downloaded);
        Assert.True(shutdown.Ok);
        Assert.False(serviceIsUp);
    }
}

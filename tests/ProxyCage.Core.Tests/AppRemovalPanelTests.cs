using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ProxyCage.Core.Tests;

public sealed class AppRemovalPanelTests
{
    [Fact]
    public async Task Removing_last_app_stops_a_running_tunnel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-remove-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "test-app");
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        var cfg = new CehoConfig();
        cfg.Apps.Add(new AppEntry { Name = "test", Folder = folder });
        cfg.Save(configPath);
        var stopped = 0;
        using var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        var web = new WebServer(configPath,
            () => new WebServer.ControlState(Volatile.Read(ref stopped) == 0, null, null, null, false),
            _ => { })
        {
            OnStop = () =>
            {
                Interlocked.Increment(ref stopped);
                return Task.FromResult<string?>(null);
            },
        };
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        try
        {
            web.Start(port);
            using var reply = await http.PostAsync("/apps/remove", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["tab"] = "apps", ["folder"] = folder }));
            Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
            var location = new Uri(http.BaseAddress!, reply.Headers.Location!);
            var job = System.Web.HttpUtility.ParseQueryString(location.Query)["job"];
            Assert.False(string.IsNullOrEmpty(job));
            for (var i = 0; i < 30 && Volatile.Read(ref stopped) == 0; i++)
                await Task.Delay(50);
            Assert.Equal(1, Volatile.Read(ref stopped));
            Assert.Empty(CehoConfig.Load(configPath).Apps);
            using var result = JsonDocument.Parse(await http.GetStringAsync($"/job?id={job}"));
            Assert.Equal("done", result.RootElement.GetProperty("state").GetString());
        }
        finally
        {
            web.Stop();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

using System.Net;
using System.Net.Sockets;

namespace ProxyCage.Core.Tests;

public sealed class WebOriginTests
{
    [Theory]
    [InlineData("Origin", "https://foreign.example", "/api")]
    [InlineData("Origin", "null", "/api")]
    [InlineData("Sec-Fetch-Site", "cross-site", "/api")]
    [InlineData("Referer", "https://foreign.example/page", "/api")]
    [InlineData("Origin", "https://foreign.example", "/apps/pick")]
    public async Task Foreign_site_cannot_execute_commands_but_local_cli_can(string header, string value, string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-origin-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "config.json");
        new CehoConfig().Save(config);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var calls = 0;
        var server = new WebServer(config, () => new(false, null, null, null, false), _ => { });
        server.OnApiCommand = _ => { calls++; return Task.FromResult((true, "ok")); };
        server.Start(port);
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(path == "/apps/pick" ? HttpMethod.Get : HttpMethod.Post, $"http://127.0.0.1:{port}{path}")
            { Content = new StringContent("status") };
            request.Headers.Add(header, value);
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, calls);
            using var local = await http.PostAsync($"http://127.0.0.1:{port}/api", new StringContent("status"));
            Assert.Equal(HttpStatusCode.OK, local.StatusCode);
            Assert.Equal(1, calls);
            using var sameOrigin = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api")
            { Content = new StringContent("status") };
            sameOrigin.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            using var sameResponse = await http.SendAsync(sameOrigin);
            Assert.Equal(HttpStatusCode.OK, sameResponse.StatusCode);
            Assert.Equal(2, calls);
        }
        finally { server.Stop(); Directory.Delete(root, true); }
    }
}

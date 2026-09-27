using System.Net;
using System.Net.Sockets;

namespace ProxyCage.Core.Tests;

public class PasswordHeaderTests
{
    [Fact]
    public void Base64_header_wins_over_the_plain_one()
    {
        Assert.Equal("секрет123", Auth.FromHeaders("?????123", Auth.Encode("секрет123")));
        Assert.Equal("ascii", Auth.FromHeaders("ascii", null));
        Assert.Equal("ascii", Auth.FromHeaders("ascii", "не base64"));
        Assert.Null(Auth.FromHeaders(null, null));
    }

    [Fact]
    public async Task Panel_takes_a_russian_password_from_the_command_line()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-pass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        var cfg = new CehoConfig();
        Auth.SetPassword(cfg, "секрет123");
        cfg.Save(configPath);

        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var web = new WebServer(configPath,
            () => new WebServer.ControlState(false, null, null, null, false), _ => { })
        {
            OnApiCommand = args => Task.FromResult((true, string.Join(" ", args))),
        };
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        try
        {
            web.Start(port);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api")
            {
                Content = new StringContent("status"),
            };
            Assert.False(Auth.HeaderSafe("секрет123"));
            request.Headers.TryAddWithoutValidation(Auth.PasswordHeaderBase64, Auth.Encode("секрет123"));

            using var reply = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            Assert.Contains("status", await reply.Content.ReadAsStringAsync());
        }
        finally
        {
            web.Stop();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

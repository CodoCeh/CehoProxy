using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ProxyCage.Core.Tests;

public sealed class CountryRefreshPanelTests
{
    [Fact]
    public async Task Refresh_replaces_stale_country_in_the_visible_pool()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-country-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        var cfg = new CehoConfig();
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "test", Url = "https://example.invalid/sub" });
        cfg.Save(configPath);
        var stale = new ProxyNode
        {
            Tag = "n001", Protocol = ProxyProtocol.Vless, Server = "edge.example", Port = 443,
            Credential = "test", Remark = "test",
        };
        var fresh = new ProxyNode
        {
            Tag = "n001", Protocol = ProxyProtocol.Vless, Server = "edge.example", Port = 443,
            Credential = "test", Remark = "test", CountryCode = "US", CountryName = "США",
        };
        var web = new WebServer(configPath, () => new(false, null, null, null, false), _ => { })
        {
            OnPool = _ => Task.FromResult<IReadOnlyList<ProxyNode>>(new[] { stale }),
            OnCountries = _ => Task.FromResult<IReadOnlyList<NodeProbe.CountryRow>>(
                NodeCountryService.Group(new[] { fresh })),
        };
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        try
        {
            web.Start(port);
            for (var i = 0; i < 30; i++)
            {
                var page = await http.GetStringAsync("/?tab=exit");
                if (page.Contains("name=all value=\"??\"")) break;
                await Task.Delay(50);
            }
            Assert.Contains("name=all value=\"??\"", await http.GetStringAsync("/?tab=exit"));

            using var reply = await http.PostAsync("/countries/refresh", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["tab"] = "exit" }));
            Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
            var location = new Uri(http.BaseAddress!, reply.Headers.Location!);
            var job = System.Web.HttpUtility.ParseQueryString(location.Query)["job"];
            Assert.False(string.IsNullOrEmpty(job));
            for (var i = 0; i < 30; i++)
            {
                using var result = JsonDocument.Parse(await http.GetStringAsync($"/job?id={job}"));
                if (result.RootElement.GetProperty("state").GetString() == "done") break;
                await Task.Delay(50);
            }
            Assert.Contains("name=all value=\"US\"", await http.GetStringAsync("/?tab=exit"));
        }
        finally
        {
            web.Stop();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

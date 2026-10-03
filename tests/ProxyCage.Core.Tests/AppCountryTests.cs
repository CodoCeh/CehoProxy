using System.Net;

namespace ProxyCage.Core.Tests;

public sealed class AppCountryTests
{
    private static ProxyNode Node(string tag, string code, string name) => new()
    {
        Tag = tag, Protocol = ProxyProtocol.Vless, Server = tag + ".example", Port = 443,
        Credential = "test", Remark = tag, CountryCode = code, CountryName = name,
    };

    [Fact]
    public async Task Country_picker_pins_the_app_to_every_node_of_that_country()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-app-country-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        var folder = Path.Combine(root, "tool");
        Directory.CreateDirectory(folder);
        var cfg = new CehoConfig();
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "test", Url = "https://example.invalid/sub" });
        cfg.Apps.Add(new AppEntry { Name = "Tool", Folder = folder });
        cfg.Save(configPath);
        var nodes = new[] { Node("a1", "US", "США"), Node("a2", "US", "США"), Node("b1", "NL", "Нидерланды") };
        var web = new WebServer(configPath, () => new(false, null, null, null, false), _ => { })
        {
            OnPool = _ => Task.FromResult<IReadOnlyList<ProxyNode>>(nodes),
        };
        var port = TestPanel.Start(web);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        try
        {
            string page = "";
            for (var i = 0; i < 40 && !page.Contains("action=/apps/country"); i++)
            {
                page = await http.GetStringAsync("/?tab=apps");
                if (!page.Contains("action=/apps/country")) await Task.Delay(50);
            }
            Assert.Contains("action=/apps/country", page);
            Assert.Contains("value=\"US\"", page);
            Assert.Contains("value=\"NL\"", page);

            using var reply = await http.PostAsync("/apps/country", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["tab"] = "apps", ["folder"] = folder, ["country"] = "US" }));
            Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
            var saved = CehoConfig.Load(configPath).Apps[0].AllowedNodes;
            Assert.Equal(2, saved.Count);
            Assert.All(saved, k => Assert.Contains(nodes.Where(n => n.CountryCode == "US").Select(n => n.Key), x => x == k));

            using var clear = await http.PostAsync("/apps/country", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["tab"] = "apps", ["folder"] = folder, ["country"] = "" }));
            Assert.Empty(CehoConfig.Load(configPath).Apps[0].AllowedNodes);

            using var rec = await http.PostAsync("/apps/add-recommended", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["tab"] = "apps" }));
            Assert.Equal(HttpStatusCode.SeeOther, rec.StatusCode);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}

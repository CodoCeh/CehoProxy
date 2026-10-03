using System.Net;

namespace ProxyCage.Core.Tests;

public sealed class NodeChoiceTests
{
    private static ProxyNode Node(string tag, string code, string remark) => new()
    {
        Tag = tag, Protocol = ProxyProtocol.Vless, Server = tag + ".example", Port = 443,
        Credential = "test", Remark = remark, CountryCode = code, CountryName = code,
    };

    [Theory]
    [InlineData("[TOP 03] Албания-1 G+", true)]
    [InlineData("Finland Gemini", true)]
    [InlineData("Antigravity-DE", true)]
    [InlineData("Германия-4", false)]
    [InlineData("EG+X", false)]
    [InlineData("Google", false)]
    public void Provider_marks_for_google_services_are_recognised(string remark, bool expected) =>
        Assert.Equal(expected, NodeChoice.IsGoogleMarked(Node("n", "AL", remark)));

    [Fact]
    public void Next_skips_current_unsuitable_and_blocked_nodes_and_prefers_another_country()
    {
        var a = Node("a", "NL", "a"); var b = Node("b", "NL", "b"); var c = Node("c", "DE", "c"); var d = Node("d", "FI", "d");
        var cfg = new CehoConfig();
        cfg.NodeLatency[b.Key] = 10; cfg.NodeLatency[c.Key] = 90; cfg.NodeLatency[d.Key] = 50;
        cfg.BlockedNodes.Add(d.Key);
        var app = new AppEntry { Name = "Tool", Folder = "/x", AllowedNodes = [a.Key] };
        var next = NodeChoice.Next(app, [a, b, c, d], cfg);
        Assert.Equal("c", next?.Tag);
        app.UnsuitableNodes.Add(c.Key);
        Assert.Equal("b", NodeChoice.Next(app, [a, b, c, d], cfg)?.Tag);
        app.UnsuitableNodes.Add(b.Key);
        Assert.Null(NodeChoice.Next(app, [a, b, c, d], cfg));
    }

    [Fact]
    public async Task Panel_rotates_pins_google_marked_nodes_and_resets()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-node-choice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        var folder = Path.Combine(root, "tool");
        Directory.CreateDirectory(folder);
        var cfg = new CehoConfig();
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "test", Url = "https://example.invalid/sub" });
        cfg.Apps.Add(new AppEntry { Name = "Tool", Folder = folder });
        cfg.Save(configPath);
        var nodes = new[] { Node("a", "NL", "Нидерланды"), Node("b", "AL", "Албания G+"), Node("c", "DE", "Германия") };
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
            for (var i = 0; i < 40 && !page.Contains("action=/apps/node-next"); i++)
            {
                page = await http.GetStringAsync("/?tab=apps");
                if (!page.Contains("action=/apps/node-next")) await Task.Delay(50);
            }
            Assert.Contains("action=/apps/node-next", page);
            Assert.Contains("action=/apps/node-tag", page);

            Task<HttpResponseMessage> Post(string path) => http.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
            { ["tab"] = "apps", ["folder"] = folder }));

            using (var tag = await Post("/apps/node-tag"))
            {
                Assert.Equal(HttpStatusCode.SeeOther, tag.StatusCode);
                Assert.Equal([nodes[1].Key], CehoConfig.Load(configPath).Apps[0].AllowedNodes);
            }
            using (var next = await Post("/apps/node-next"))
            {
                Assert.Equal(HttpStatusCode.SeeOther, next.StatusCode);
                var app = CehoConfig.Load(configPath).Apps[0];
                Assert.Single(app.AllowedNodes);
                Assert.NotEqual(nodes[1].Key, app.AllowedNodes[0]);
                Assert.Contains(nodes[1].Key, app.UnsuitableNodes);
            }
            using (var reset = await Post("/apps/node-reset"))
            {
                var app = CehoConfig.Load(configPath).Apps[0];
                Assert.Empty(app.AllowedNodes);
                Assert.Empty(app.UnsuitableNodes);
            }
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}

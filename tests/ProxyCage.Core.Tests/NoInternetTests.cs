using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class NoInternetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-offline-" + Guid.NewGuid().ToString("N")[..8]);

    public NoInternetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static IReadOnlyList<ProxyNode> Nodes() =>
        SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static string Folder(string name) =>
        Os.IsWindows ? $@"C:\Games\{name}" : $"/tmp/{name}";

    private static List<string> Regexes(JsonNode? rule) =>
        rule?["process_path_regex"] is JsonArray a ? a.Select(x => (string)x!).ToList() : new();

    [Fact]
    public void An_app_without_internet_is_rejected_before_any_other_rule()
    {
        var offline = new AppEntry { Name = "offline", Folder = Folder("Offline"), AllowedNodes = { "x" } };
        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "online", Folder = Folder("Online") }, offline },
            DirectSites = { "example.org" },
        };
        offline.NoInternet = true;
        var marks = AppDetector.ToRegexes(offline);

        var root = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg))!;
        var rules = root["route"]!["rules"]!.AsArray();

        Assert.Equal("sniff", (string?)rules[0]!["action"]);
        Assert.Equal("reject", (string?)rules[1]!["action"]);
        Assert.Null(rules[1]!["protocol"]);
        Assert.Equal(marks, Regexes(rules[1]));

        foreach (var rule in rules.Skip(2))
            Assert.DoesNotContain(Regexes(rule), marks.Contains);
        Assert.DoesNotContain(root["outbounds"]!.AsArray(), o =>
            ((string?)o!["tag"] ?? "").StartsWith("proxy-app-", StringComparison.Ordinal));
        Assert.Contains(rules, r => (string?)r?["outbound"] == "proxy" && r["process_path_regex"] is JsonArray);
    }

    [Fact]
    public void Without_the_switch_nothing_changes()
    {
        var cfg = new CehoConfig { Apps = { new AppEntry { Name = "online", Folder = Folder("Online") } } };
        var rules = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg))!["route"]!["rules"]!.AsArray();

        Assert.NotEqual("reject", (string?)rules[1]!["action"]);
    }

    [Fact]
    public async Task Panel_switches_the_internet_off_and_back()
    {
        var configPath = Path.Combine(_root, "config.json");
        var folder = Folder("Offline");
        new CehoConfig { Apps = { new AppEntry { Name = "Offline", Folder = folder } } }.Save(configPath);

        var web = new WebServer(configPath, () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        var port = TestPanel.Start(web);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        try
        {
            var page = await http.GetStringAsync("/?tab=apps&tunnel=" + Uri.EscapeDataString(folder));
            Assert.Contains("action=/apps/offline", page);
            Assert.Contains(WebUtility.HtmlEncode(Strings.T("ru", "app_offline_add")), page);

            using var off = await http.PostAsync("/apps/offline", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["tab"] = "apps", ["folder"] = folder, ["enable"] = "1" }));
            Assert.Equal(HttpStatusCode.SeeOther, off.StatusCode);
            Assert.True(CehoConfig.Load(configPath).Apps[0].NoInternet);
            Assert.Contains(WebUtility.HtmlEncode("Интернет запрещён"), await http.GetStringAsync("/?tab=apps"));

            using var on = await http.PostAsync("/apps/offline", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["tab"] = "apps", ["folder"] = folder }));
            Assert.False(CehoConfig.Load(configPath).Apps[0].NoInternet);
        }
        finally
        {
            web.Stop();
        }
    }

    [Fact]
    public void Tray_task_runs_for_every_signed_in_user_without_a_time_limit()
    {
        var xml = XDocument.Parse(TrayInstaller.WindowsSessionTaskXml(@"C:\ProgramData\CehoProxy\cehoproxy-tray.exe"));
        XNamespace t = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal("S-1-5-32-545", xml.Descendants(t + "GroupId").Single().Value);
        Assert.Equal("LeastPrivilege", xml.Descendants(t + "RunLevel").Single().Value);
        Assert.Equal("PT0S", xml.Descendants(t + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("Parallel", xml.Descendants(t + "MultipleInstancesPolicy").Single().Value);
        Assert.Equal(@"C:\ProgramData\CehoProxy\cehoproxy-tray.exe", xml.Descendants(t + "Command").Single().Value);
    }
}

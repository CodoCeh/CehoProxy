using System.Net;
using System.Text.RegularExpressions;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Живая жалоба: «на дашборде периодически смещаются черты». Ехало от двух вещей — страница
/// проигрывала появление разделов на каждом самообновлении, а столбцы таблиц браузер мерил
/// по содержимому, и на новых числах они вставали иначе. Здесь стережём и то, и другое.
/// </summary>
public class PanelLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ceho-layout-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly WebServer _web;
    private readonly HttpClient _http;
    private readonly TaskCompletionSource _gate = new();

    public PanelLayoutTests()
    {
        Directory.CreateDirectory(_root);

        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "app", Folder = Os.IsWindows ? @"C:\Games\App" : "/tmp" } },
            Subscriptions = { new SubscriptionEntry { Name = "sub", Url = "https://example.invalid/sub" } },
        };
        cfg.Save(Path.Combine(_root, "config.json"));

        var port = FreePort();
        _web = new WebServer(
            Path.Combine(_root, "config.json"),
            () => new WebServer.ControlState(false, null, null, null, false),
            _ => { })
        {
            OnPool = async _ =>
            {
                await _gate.Task;
                return Nodes();
            },
        };
        _web.Start(port);

        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
    }

    public void Dispose()
    {
        _gate.TrySetResult();
        _web.Stop();
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static IReadOnlyList<ProxyNode> Nodes() =>
        SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [Fact]
    public async Task Every_table_has_the_column_widths_written_for_it()
    {
        foreach (var tab in new[] { "state", "apps", "subs", "exit", "log", "help" })
        {
            var page = await _http.GetStringAsync($"/?tab={tab}");

            foreach (Match table in Regex.Matches(page, "(?<before>.{20})<table(?<attrs>[^>]*)>"))
            {
                var css = Regex.Match(table.Groups["attrs"].Value, @"class=(?<name>[\w-]+)");
                Assert.True(css.Success, $"таблица на вкладке {tab} без класса: столбцы будут плясать");

                var name = css.Groups["name"].Value;
                Assert.Contains($"table.{name} th:nth-child", WebUi.Css);

                // Без обёртки узкий экран сминает столбцы до переноса по буквам.
                Assert.EndsWith("<div class=scroll>", table.Groups["before"].Value);
            }
        }
    }

    [Fact]
    public async Task A_page_that_refreshes_itself_does_not_replay_the_intro()
    {
        var reply = await _http.PostAsync("/pool/refresh",
            new StringContent("tab=exit", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"));

        var where = reply.Headers.Location?.OriginalString ?? "";
        Assert.Contains("job=", where);

        var page = await _http.GetStringAsync(where);

        Assert.Contains("<body class=busy>", page);
        Assert.Contains("body.busy section", WebUi.Css);

        _gate.SetResult();
    }

    [Fact]
    public void Cells_stay_cells()
    {
        // Ячейка с display:flex выпадает из табличной раскладки: она не тянется на высоту
        // строки, и её нижняя черта висит выше соседних. Ровно это и было видно на дашборде.
        foreach (Match rule in Regex.Matches(WebUi.Css, @"(?<sel>[^{}]+)\{(?<body>[^}]*)\}"))
        {
            var selector = rule.Groups["sel"].Value;
            if (!Regex.IsMatch(selector, @"(^|[\s,>])t[dh]\b")) continue;

            Assert.DoesNotContain("display:flex", rule.Groups["body"].Value);
            Assert.DoesNotContain("display:grid", rule.Groups["body"].Value);
        }
    }

    [Fact]
    public void Appearance_moves_nothing_sideways_or_down()
    {
        // Разделы должны только проявляться. Сдвиг на каждом обновлении и читался как «черты едут».
        var rise = Regex.Match(WebUi.Css, @"@keyframes rise\{(?<body>[^}]*\}[^}]*)\}");

        Assert.True(rise.Success, "появление разделов должно остаться описанным");
        Assert.DoesNotContain("transform", rise.Groups["body"].Value);
    }
}

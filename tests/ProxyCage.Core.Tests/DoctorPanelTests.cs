using System.Net;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Кнопки осмотра в панели: нажатие ставит фоновое дело, а страница потом
/// показывает его итог. Проверяем живьём, поднимая настоящую панель на свободном порту.
/// </summary>
public class DoctorPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ceho-panel-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly WebServer _web;
    private readonly HttpClient _http;
    private readonly int _port;

    public DoctorPanelTests()
    {
        Directory.CreateDirectory(_root);

        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "app", Folder = Os.IsWindows ? @"C:\Games\App" : "/tmp" } },
            Subscriptions = { new SubscriptionEntry { Name = "sub", Url = "https://example.invalid/sub" } },
        };
        cfg.Save(Path.Combine(_root, "config.json"));

        _port = FreePort();
        _web = new WebServer(
            Path.Combine(_root, "config.json"),
            () => new WebServer.ControlState(false, null, null, null, false),
            _ => { })
        {
            OnPool = _ => Task.FromResult(Nodes()),
            OnApply = _ =>
            {
                File.WriteAllText(Path.Combine(_root, "singbox.json"), "{}");
                return Task.FromResult("правила пересобраны");
            },
        };
        _web.Start(_port);

        // Ответ на нажатие — переезд на страницу с номером дела: его и надо прочитать.
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}"),
        };
    }

    public void Dispose()
    {
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

    private async Task<string> PressAsync(string action)
    {
        var reply = await _http.PostAsync(action,
            new StringContent("tab=doctor", System.Text.Encoding.UTF8,
                "application/x-www-form-urlencoded"));

        var where = reply.Headers.Location?.OriginalString ?? "";
        var id = System.Web.HttpUtility.ParseQueryString(where.Split('?').Last())["job"];
        Assert.False(string.IsNullOrEmpty(id), $"панель не завела дело: {where}");

        for (var i = 0; i < 100; i++)
        {
            var json = await _http.GetStringAsync($"/job?id={Uri.EscapeDataString(id!)}");
            if (!json.Contains("\"running\"")) return json;
            await Task.Delay(100);
        }

        throw new TimeoutException("осмотр не закончился");
    }

    [Fact]
    public async Task The_check_button_fills_the_page_with_findings()
    {
        var job = await PressAsync("/doctor/check");
        Assert.Contains("\"done\"", job);

        var page = await _http.GetStringAsync("/?tab=doctor");

        Assert.Contains(Strings.T("ru", "doc_rules_missing"), page);
        Assert.Contains(Strings.T("ru", "doc_subs_ok", Nodes().Count), page);
        Assert.DoesNotContain(Strings.T("ru", "doc_never"), page);
    }

    [Fact]
    public async Task The_fix_button_rebuilds_the_rules_and_says_what_it_did()
    {
        await PressAsync("/doctor/fix");

        var page = await _http.GetStringAsync("/?tab=doctor");

        Assert.True(File.Exists(Path.Combine(_root, "singbox.json")), "правила должны появиться");
        Assert.Contains(Strings.T("ru", "doc_did_title"), page);
        Assert.Contains("правила пересобраны", page);
        Assert.DoesNotContain(Strings.T("ru", "doc_rules_missing"), page);
    }

    [Fact]
    public async Task The_doctor_tab_is_reachable_from_the_menu()
    {
        var page = await _http.GetStringAsync("/");

        Assert.Contains("/?tab=doctor", page);
        Assert.Contains(Strings.T("ru", "nav_doctor"), page);
    }
}

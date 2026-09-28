using System.Net;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class EngineUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-engine-" + Guid.NewGuid().ToString("N")[..8]);

    public EngineUpdateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string FakeEngine(string path, string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"#!/bin/sh\necho sing-box version {version}\n");
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return path;
    }

    [Fact]
    public void Only_an_engine_older_than_the_tested_one_is_outdated()
    {
        Assert.True(Installer.EngineOutdated("1.13.5"));
        Assert.False(Installer.EngineOutdated(Installer.EngineVersion));
        Assert.False(Installer.EngineOutdated("99.0.0"));
        Assert.False(Installer.EngineOutdated(null));
    }

    [Fact]
    public void Engine_version_is_read_from_its_own_answer()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Equal("1.13.5", Installer.EngineVersionOf(FakeEngine(Path.Combine(_root, "sing-box"), "1.13.5")));
    }

    [Fact]
    public void A_swapped_engine_keeps_the_old_one_for_a_rollback()
    {
        if (OperatingSystem.IsWindows()) return;

        var engine = FakeEngine(Path.Combine(_root, Os.EngineFileName), "1.13.5");
        var fresh = FakeEngine(Path.Combine(Installer.EngineStagingDir(_root), "sing-box-x", Os.SingBoxFileName),
            Installer.EngineVersion);

        Installer.SwapEngine(_root, fresh);

        Assert.Equal(Installer.EngineVersion, Installer.EngineVersionOf(engine));
        Assert.Equal("1.13.5", Installer.EngineVersionOf(engine + ".old"));
        Assert.False(File.Exists(fresh));

        Installer.RestoreEngine(_root);
        Installer.DropEngineStaging(_root);

        Assert.Equal("1.13.5", Installer.EngineVersionOf(engine));
        Assert.False(Directory.Exists(Installer.EngineStagingDir(_root)));
    }

    [Fact]
    public async Task Panel_offers_the_update_only_for_an_old_engine_and_keeps_the_switch()
    {
        if (OperatingSystem.IsWindows()) return;

        var configPath = Path.Combine(_root, "config.json");
        new CehoConfig { PanelMode = CehoConfig.PanelModeSimple }.Save(configPath);
        FakeEngine(Path.Combine(_root, Os.EngineFileName), "1.13.5");

        var web = new WebServer(configPath, () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        var port = TestPanel.Start(web);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        try
        {
            var page = await http.GetStringAsync("/?tab=state");
            Assert.Contains(WebUtility.HtmlEncode(Strings.T("ru", "engine_line_old", "1.13.5", Installer.EngineVersion)), page);
            Assert.Contains("action=/engine/update", page);
            Assert.Contains(WebUtility.HtmlEncode(Strings.T("ru", "engine_auto_on")), page);
            Assert.True(CehoConfig.Load(configPath).EngineAutoUpdate);

            using var off = await http.PostAsync("/engine/autoupdate",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["tab"] = "state" }));
            Assert.Equal(HttpStatusCode.SeeOther, off.StatusCode);
            Assert.False(CehoConfig.Load(configPath).EngineAutoUpdate);
        }
        finally
        {
            web.Stop();
            http.Dispose();
        }
    }

    [Fact]
    public async Task Panel_has_no_update_button_for_the_tested_engine()
    {
        if (OperatingSystem.IsWindows()) return;

        var configPath = Path.Combine(_root, "config.json");
        new CehoConfig { PanelMode = CehoConfig.PanelModeSimple }.Save(configPath);
        FakeEngine(Path.Combine(_root, Os.EngineFileName), Installer.EngineVersion);

        var web = new WebServer(configPath, () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        var port = TestPanel.Start(web);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        try
        {
            var page = await http.GetStringAsync("/?tab=state");
            Assert.Contains(WebUtility.HtmlEncode(Strings.T("ru", "engine_line", Installer.EngineVersion)), page);
            Assert.DoesNotContain("action=/engine/update", page);
        }
        finally
        {
            web.Stop();
        }
    }
}

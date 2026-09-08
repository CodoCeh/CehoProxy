using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class AppDisplayNameTests
{
    [Fact]
    public void Label_falls_back_to_Name_when_DisplayName_is_empty()
    {
        var app = new AppEntry { Name = "Application", Folder = "/tmp/chrome" };
        Assert.Equal("Application", app.Label);
    }

    [Fact]
    public void Label_uses_DisplayName_when_set()
    {
        var app = new AppEntry
        {
            Name = "Application",
            DisplayName = "Google Chrome",
            Folder = "/tmp/chrome",
        };
        Assert.Equal("Google Chrome", app.Label);
    }

    [Fact]
    public void DisplayName_round_trips_through_config()
    {
        var path = Path.Combine(Path.GetTempPath(), "chp-display-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            var cfg = new CehoConfig();
            cfg.Apps.Add(new AppEntry
            {
                Name = "Application",
                DisplayName = "Google Chrome",
                Folder = Os.IsWindows ? @"C:\Apps\Chrome" : "/Applications/Google Chrome.app",
            });
            cfg.Save(path);

            var loaded = CehoConfig.Load(path);
            Assert.Equal("Google Chrome", loaded.Apps[0].DisplayName);
            Assert.Equal("Google Chrome", loaded.Apps[0].Label);
            Assert.Equal("Application", loaded.Apps[0].Name);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Old_config_without_DisplayName_still_works()
    {
        var path = Path.Combine(Path.GetTempPath(), "chp-display-old-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "Apps": [
                    { "Name": "Cursor", "Folder": "/tmp/cursor", "Enabled": true }
                  ]
                }
                """);

            var loaded = CehoConfig.Load(path);
            Assert.Null(loaded.Apps[0].DisplayName);
            Assert.Equal("Cursor", loaded.Apps[0].Label);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Routing_still_uses_Folder_not_DisplayName()
    {
        var folder = Os.IsWindows ? @"C:\Apps\Chrome" : "/tmp/chrome";
        var cfg = new CehoConfig
        {
            Apps =
            {
                new AppEntry
                {
                    Name = "Application",
                    DisplayName = "Google Chrome",
                    Folder = folder,
                },
                new AppEntry
                {
                    Name = "Application",
                    DisplayName = "Microsoft Edge",
                    Folder = Os.IsWindows ? @"C:\Apps\Edge" : "/tmp/edge",
                },
            },
        };

        var chrome = AppDetector.ToRegex(cfg.Apps[0]);
        var edge = AppDetector.ToRegex(cfg.Apps[1]);
        Assert.NotEqual(chrome, edge);
        Assert.Matches(chrome, folder + (Os.IsWindows ? @"\Application.exe" : "/Application.exe"));
        Assert.DoesNotMatch(chrome, cfg.Apps[1].Folder + "/Application.exe");
    }

    [Fact]
    public void Two_apps_with_same_Name_are_distinguishable_by_DisplayName()
    {
        var cfg = new CehoConfig
        {
            Apps =
            {
                new AppEntry { Name = "Application", DisplayName = "Google Chrome", Folder = "/a" },
                new AppEntry { Name = "Application", DisplayName = "Microsoft Edge", Folder = "/b" },
            },
        };

        Assert.Equal("Google Chrome", cfg.Apps[0].Label);
        Assert.Equal("Microsoft Edge", cfg.Apps[1].Label);
        Assert.NotEqual(cfg.Apps[0].Label, cfg.Apps[1].Label);
    }

    [Fact]
    public void Clearing_DisplayName_restores_detected_Name()
    {
        var app = new AppEntry { Name = "Application", DisplayName = "Google Chrome" };
        app.DisplayName = null;
        Assert.Equal("Application", app.Label);
    }
}

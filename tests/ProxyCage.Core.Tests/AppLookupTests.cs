using ProxyCage.Core;
using Xunit;

namespace ProxyCage.Core.Tests;

public class AppLookupTests
{
    private static AppEntry App(string folder, string? display = null) => new() { Folder = folder, DisplayName = display ?? "" };

    [Fact]
    public void Finds_by_file_name_ignoring_case()
    {
        var apps = new[] { App("/usr/bin/wget"), App("/opt/other") };

        var found = AppLookup.ByName(apps, "WGET");

        Assert.Equal("/usr/bin/wget", Assert.Single(found).Folder);
    }

    [Fact]
    public void Finds_by_display_name()
    {
        var apps = new[] { App("/usr/bin/wget", "Загрузчик"), App("/opt/other") };

        Assert.Equal("/usr/bin/wget", Assert.Single(AppLookup.ByName(apps, "загрузчик")).Folder);
    }

    [Fact]
    public void Reports_every_match_when_name_is_shared()
    {
        var apps = new[] { App("/usr/bin/wget"), App("/opt/tools/wget") };

        Assert.Equal(2, AppLookup.ByName(apps, "wget").Count);
    }

    [Fact]
    public void Unknown_name_finds_nothing() =>
        Assert.Empty(AppLookup.ByName(new[] { App("/usr/bin/wget") }, "curl"));
}

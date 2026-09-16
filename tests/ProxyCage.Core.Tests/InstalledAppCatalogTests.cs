using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class InstalledAppCatalogTests
{
    [Fact]
    public void Linux_desktop_entry_uses_localized_name_and_try_exec()
    {
        const string desktop = """
            [Desktop Entry]
            Type=Application
            Name=Example Browser
            Name[ru]=Пример Браузера
            TryExec=example-browser
            Exec=example-browser --new-window %U
            """;

        var app = InstalledAppCatalog.ParseDesktopEntry(desktop,
            command => command == "example-browser" ? "/opt/example/example-browser" : null, "ru");

        Assert.NotNull(app);
        Assert.Equal("Пример Браузера", app.Name);
        Assert.Equal("/opt/example/example-browser", app.Path);
    }

    [Theory]
    [InlineData("NoDisplay=true")]
    [InlineData("Hidden=true")]
    [InlineData("Type=Link")]
    public void Linux_hidden_or_non_application_entries_are_ignored(string property)
    {
        var desktop = $"[Desktop Entry]\nName=Hidden\nExec=/usr/bin/hidden\n{property}\n";

        var app = InstalledAppCatalog.ParseDesktopEntry(desktop, command => command, "en");

        Assert.Null(app);
    }

    [Theory]
    [InlineData("\"/opt/My App/app\" --open %U", "/opt/My App/app")]
    [InlineData("/usr/bin/firefox %u", "/usr/bin/firefox")]
    public void Linux_exec_field_extracts_the_executable(string exec, string expected) =>
        Assert.Equal(expected, InstalledAppCatalog.FirstCommand(exec));

    [Theory]
    [InlineData("env DESKTOP_STARTUP_ID=1 /opt/app/bin/app %U", "/opt/app/bin/app")]
    [InlineData("flatpak run org.example.App", "")]
    [InlineData("bash -c app", "")]
    public void Linux_shared_launchers_are_skipped(string exec, string expected) =>
        Assert.Equal(expected, InstalledAppCatalog.ExecutableCommand(exec));

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\",0", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\tool.exe --background", "C:\\Tools\\tool.exe")]
    public void Windows_display_icon_extracts_executable(string value, string expected) =>
        Assert.Equal(expected, InstalledAppCatalog.CleanWindowsExecutable(value));
}

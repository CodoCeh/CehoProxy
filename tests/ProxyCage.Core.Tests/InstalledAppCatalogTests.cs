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
            Icon=example-browser
            TryExec=example-browser
            Exec=example-browser --new-window %U
            """;

        var app = InstalledAppCatalog.ParseDesktopEntry(desktop,
            command => command == "example-browser" ? "/opt/example/example-browser" : null, "ru");

        Assert.NotNull(app);
        Assert.Equal("Пример Браузера", app.Name);
        Assert.Equal("/opt/example/example-browser", app.Path);
        Assert.Equal("example-browser", app.Icon);
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

    [Fact]
    public void Service_sees_apps_of_every_user()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            foreach (var d in new[] { "alice", "bob", "Shared", ".hidden" }) Directory.CreateDirectory(Path.Combine(root, d));

            var asRoot = InstalledAppCatalog.UserHomes("/var/root", elevated: true, root).Select(Path.GetFileName).Order().ToList();
            Assert.Equal(new[] { "alice", "bob", "root" }, asRoot);

            var asUser = InstalledAppCatalog.UserHomes("/Users/alice", elevated: false, root).ToList();
            Assert.Equal(new[] { "/Users/alice" }, asUser);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Store_packages_are_listed_by_latest_version_without_frameworks_or_hidden_system_parts()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            void Package(string dir, string manifest, string? exe = null)
            {
                var full = Path.Combine(root, dir);
                Directory.CreateDirectory(full);
                File.WriteAllText(Path.Combine(full, "AppxManifest.xml"), manifest);
                if (exe is not null) File.WriteAllText(Path.Combine(full, exe), "");
            }
            const string ns = "xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\" xmlns:uap=\"http://schemas.microsoft.com/appx/manifest/uap/windows10\"";
            string App(string display, string exe) =>
                $"<Package {ns}><Properties><DisplayName>{display}</DisplayName></Properties>" +
                $"<Applications><Application Id=\"App\" Executable=\"{exe}\"><uap:VisualElements DisplayName=\"{display}\"/></Application></Applications></Package>";

            Package("TelegramMessengerLLP.TelegramDesktop_5.1.0.0_x64__t4vj0pshhgkwm", App("Telegram Desktop", "Telegram.exe"), "Telegram.exe");
            Package("TelegramMessengerLLP.TelegramDesktop_5.10.2.0_x64__t4vj0pshhgkwm", App("Telegram Desktop", "Telegram.exe"), "Telegram.exe");
            Package("Vendor.Tool_1.0.0.0_x64__abc", App("ms-resource:AppName", "Tool.exe"), "Tool.exe");
            Package("Microsoft.VCLibs.140.00_14.0.0.0_x64__8wekyb3d8bbwe",
                $"<Package {ns}><Properties><DisplayName>VCLibs</DisplayName><Framework>true</Framework></Properties></Package>");
            Package("Vendor.Tool_1.0.0.0_neutral_split.scale-100_abc", $"<Package {ns}><Properties><DisplayName>x</DisplayName></Properties></Package>");
            Package("Microsoft.BingNews_4.1.0.0_x64__8wekyb3d8bbwe",
                $"<Package {ns}><Identity Name=\"Microsoft.BingNews\" Publisher=\"CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US\"/>" +
                "<Properties><DisplayName>ms-resource:AppName</DisplayName></Properties>" +
                "<Applications><Application Id=\"App\" Executable=\"News.exe\"><uap:VisualElements DisplayName=\"ms-resource:AppName\"/></Application></Applications></Package>", "News.exe");
            Package("Vendor.Helper_1.0.0.0_x64__abc",
                $"<Package {ns}><Properties><DisplayName>Helper</DisplayName></Properties>" +
                "<Applications><Application Id=\"App\" Executable=\"Helper.exe\"><uap:VisualElements DisplayName=\"Helper\" AppListEntry=\"none\"/></Application></Applications></Package>", "Helper.exe");

            var found = InstalledAppCatalog.DetectWindowsStore(root).OrderBy(e => e.Name).ToList();

            Assert.Equal(new[] { "Telegram Desktop", "Tool" }, found.Select(e => e.Name));
            Assert.Contains("_5.10.2.0_", found[0].Path);
            Assert.All(found, e => Assert.Equal("Microsoft Store", e.Source));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Only_folders_inside_count_as_under()
    {
        var win = Path.Combine(Path.GetTempPath(), "Win");
        Assert.True(InstalledAppCatalog.IsUnder(Path.Combine(win, "System32", "wab.exe"), win));
        Assert.False(InstalledAppCatalog.IsUnder(Path.Combine(Path.GetTempPath(), "WinApps", "a.exe"), win));
        Assert.False(InstalledAppCatalog.IsUnder(Path.Combine(win, "a.exe"), ""));
    }

    [Theory]
    [InlineData("Opera Stable 135.0.5973.92", "Opera Stable")]
    [InlineData("7-Zip 24.08 (x64)", "7-Zip 24.08 (x64)")]
    [InlineData("Notepad++ v8.6", "Notepad++")]
    [InlineData("Telegram Desktop", "Telegram Desktop")]
    [InlineData("1.2.3", "1.2.3")]
    public void Trailing_version_is_dropped_from_the_name(string raw, string expected) =>
        Assert.Equal(expected, InstalledAppCatalog.WithoutTrailingVersion(raw));

    [Fact]
    public void Launcher_next_to_a_versioned_copy_is_the_same_program()
    {
        var app = Path.Combine(Path.GetTempPath(), "Edge", "Application");
        var seen = new[] { Path.Combine(app, "154.0.4258.37", "msedge.exe") };

        Assert.True(InstalledAppCatalog.SameProgramSeen(seen, Path.Combine(app, "msedge.exe")));
        Assert.False(InstalledAppCatalog.SameProgramSeen(seen, Path.Combine(app, "other.exe")));
        Assert.False(InstalledAppCatalog.SameProgramSeen(seen, Path.Combine(Path.GetTempPath(), "Elsewhere", "msedge.exe")));
    }

    [Theory]
    [InlineData("Microsoft® Windows® Operating System", true)]
    [InlineData("Internet Explorer", true)]
    [InlineData("Microsoft Edge", false)]
    [InlineData("Opera Internet Browser", false)]
    [InlineData(null, false)]
    public void Parts_of_windows_itself_are_not_offered(string? product, bool component) =>
        Assert.Equal(component, InstalledAppCatalog.IsWindowsComponent(product));

    [Fact]
    public void Cehoproxy_itself_is_not_offered()
    {
        var home = Path.Combine(Path.GetTempPath(), "CehoProxy");
        var self = Path.Combine(home, "cehoproxy.exe");
        var bundle = Path.Combine(Path.GetTempPath(), "Apps", "CehoProxy.app");

        Assert.True(InstalledAppCatalog.IsOwn(Path.Combine(home, "libcronet.dll"), self, windows: true));
        Assert.False(InstalledAppCatalog.IsOwn(Path.Combine(home, "libcronet.dll"), self, windows: false));
        Assert.True(InstalledAppCatalog.IsOwn(bundle, Path.Combine(bundle, "Contents", "MacOS", "cehoproxy"), windows: false));
        Assert.False(InstalledAppCatalog.IsOwn(Path.Combine(Path.GetTempPath(), "Other", "app.exe"), self, windows: true));
        Assert.True(InstalledAppCatalog.IsOwn("/Applications/CehoProxy Tray.app", null, windows: false));
        Assert.False(InstalledAppCatalog.IsOwn(Path.Combine(Path.GetTempPath(), "Other", "app.exe"), null, windows: true));
    }

    [Fact]
    public void Store_program_inside_a_subfolder_finds_its_package()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var package = Path.Combine(root, "WindowsApps", "Microsoft.Paint_11.0.0.0_x64__8wekyb3d8bbwe");
            var exe = Path.Combine(package, "PaintApp", "mspaint.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(Path.Combine(package, "AppxManifest.xml"), "<Package/>");
            File.WriteAllText(exe, "");

            Assert.Equal(package, AppIcons.PackageOf(exe));
            Assert.Null(AppIcons.PackageOf(Path.Combine(root, "Other", "a.exe")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Telegram Desktop", "/x/Telegram.exe", InstalledAppCatalog.Group.Messengers)]
    [InlineData("Microsoft Teams", "/x/ms-teams.exe", InstalledAppCatalog.Group.Messengers)]
    [InlineData("ChatGPT", "/x/ChatGPT.exe", InstalledAppCatalog.Group.Ai)]
    [InlineData("Microsoft 365 Copilot", "/x/m365.exe", InstalledAppCatalog.Group.Ai)]
    [InlineData("Firefox ESR", "/usr/bin/firefox-esr", InstalledAppCatalog.Group.Browsers)]
    [InlineData("Microsoft Edge", "/x/msedge.exe", InstalledAppCatalog.Group.Browsers)]
    [InlineData("Opera Stable", "/x/opera.exe", InstalledAppCatalog.Group.Browsers)]
    [InlineData("Music", @"C:\Users\u\AppData\Local\Programs\YandexMusic\Y.Music.exe", InstalledAppCatalog.Group.Other)]
    [InlineData("Samsung Browser", "/x/samsunginternet.exe", InstalledAppCatalog.Group.Browsers)]
    [InlineData("Paint", "/x/mspaint.exe", InstalledAppCatalog.Group.Other)]
    [InlineData("Cursorless Notes", "/x/notes.exe", InstalledAppCatalog.Group.Other)]
    public void Programs_are_grouped_by_what_people_usually_tunnel(string name, string path, InstalledAppCatalog.Group group) =>
        Assert.Equal(group, InstalledAppCatalog.GroupOf(new InstalledAppCatalog.Entry(name, path, "test")));
}

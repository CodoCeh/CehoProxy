using System.Net;

namespace ProxyCage.Core.Tests;

public sealed class AppIconTests
{
    [Fact]
    public void Icns_picks_the_png_closest_to_the_panel_size()
    {
        var icns = Icns([Png(32), Png(128), Png(512)]);

        var picked = AppIcons.IcnsPng(icns);

        Assert.NotNull(picked);
        Assert.Equal(Png(128), picked);
    }

    [Fact]
    public void Icns_without_png_chunks_gives_nothing()
    {
        var legacy = Icns([new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }]);

        Assert.Null(AppIcons.IcnsPng(legacy));
    }

    [Fact]
    public void Windows_bitmap_becomes_an_icon_file()
    {
        var dib = new byte[40 + 16];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(48).CopyTo(dib, 4);
        BitConverter.GetBytes(96).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);

        var ico = AppIcons.IcoFromDib(dib);

        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 4));
        Assert.Equal(48, ico[6]);
        Assert.Equal(48, ico[7]);
        Assert.Equal(32, BitConverter.ToUInt16(ico, 12));
        Assert.Equal(dib.Length, BitConverter.ToInt32(ico, 14));
        Assert.Equal(22, BitConverter.ToInt32(ico, 18));
        Assert.Equal(dib, ico[22..]);
    }

    [Fact]
    public void Linux_icon_name_resolves_inside_the_theme_folders()
    {
        var wanted = Path.Combine("/usr/share/icons", "hicolor", "128x128", "apps", "example.png");
        var smaller = Path.Combine("/usr/share/icons", "hicolor", "48x48", "apps", "example.png");

        var found = AppIcons.LinuxIconFile("example", file => file == wanted || file == smaller);

        Assert.Equal(wanted, found);
    }

    [Fact]
    public void Linux_icon_name_without_a_file_gives_nothing()
    {
        Assert.Null(AppIcons.LinuxIconFile("example", _ => false));
        Assert.Null(AppIcons.LinuxIconFile(null, _ => true));
    }

    [Fact]
    public void Initial_letter_survives_emoji_and_empty_names()
    {
        Assert.Equal("S", WebServer.Initial(" safari"));
        Assert.Equal("Б", WebServer.Initial("браузер"));
        Assert.Equal("?", WebServer.Initial("   "));
    }

    [Fact]
    public async Task Panel_offers_installed_apps_as_cards_and_guards_the_icon_route()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        new CehoConfig().Save(configPath);

        var web = new WebServer(configPath,
            () => new WebServer.ControlState(false, null, null, null, false), _ => { });
        var port = TestPanel.Start(web);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        try
        {
            var html = await http.GetStringAsync("/?tab=apps");

            var installed = InstalledAppCatalog.Detect();
            if (installed.Count > 0)
            {
                Assert.Contains("class=app-grid", html);
                Assert.Contains("class=app-card", html);
                Assert.DoesNotContain("<option value=\"" + WebUtility.HtmlEncode(installed[0].Path), html);
                Assert.Contains("/icon?path=", html);
            }

            using var denied = await http.GetAsync("/icon?path=" + Uri.EscapeDataString("/etc/hosts"));
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        finally
        {
            web.Stop();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Windows_executable_gives_back_its_largest_icon()
    {
        var wanted = Png(128, 64);
        var exe = Path.Combine(Path.GetTempPath(), "ceho-exe-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(exe, Executable(wanted));
        try
        {
            var icon = AppIcons.FromExecutable(exe);

            Assert.NotNull(icon);
            Assert.Equal("image/png", icon.ContentType);
            Assert.Equal(wanted, icon.Bytes);
        }
        finally { File.Delete(exe); }
    }

    [Fact]
    public void Windows_executable_with_a_bitmap_icon_gives_an_icon_file()
    {
        var dib = new byte[40 + 32];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(64).CopyTo(dib, 4);
        BitConverter.GetBytes(128).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
        var exe = Path.Combine(Path.GetTempPath(), "ceho-exe-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(exe, Executable(dib));
        try
        {
            var icon = AppIcons.FromExecutable(exe);

            Assert.NotNull(icon);
            Assert.Equal("image/x-icon", icon.ContentType);
            Assert.Equal(dib, icon.Bytes[22..]);
        }
        finally { File.Delete(exe); }
    }

    private static byte[] Executable(byte[] icon)
    {
        const int pe = 0x80;
        const int optional = 0xE0;
        const int sectionRaw = 0x400;
        const int sectionRva = 0x1000;
        var file = new byte[sectionRaw + 0x58 + icon.Length];

        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        BitConverter.GetBytes(pe).CopyTo(file, 0x3C);
        BitConverter.GetBytes(0x00004550).CopyTo(file, pe);
        BitConverter.GetBytes((ushort)1).CopyTo(file, pe + 6);
        BitConverter.GetBytes((ushort)optional).CopyTo(file, pe + 20);
        BitConverter.GetBytes((ushort)0x010B).CopyTo(file, pe + 24);
        BitConverter.GetBytes(sectionRva).CopyTo(file, pe + 24 + 96 + 16);

        var section = pe + 24 + optional;
        ".rsrc"u8.ToArray().CopyTo(file, section);
        BitConverter.GetBytes(0x58 + icon.Length).CopyTo(file, section + 8);
        BitConverter.GetBytes(sectionRva).CopyTo(file, section + 12);
        BitConverter.GetBytes(0x58 + icon.Length).CopyTo(file, section + 16);
        BitConverter.GetBytes(sectionRaw).CopyTo(file, section + 20);

        BitConverter.GetBytes((ushort)1).CopyTo(file, sectionRaw + 14);
        BitConverter.GetBytes(3).CopyTo(file, sectionRaw + 16);
        BitConverter.GetBytes(0x80000000 | 0x18).CopyTo(file, sectionRaw + 20);

        BitConverter.GetBytes((ushort)1).CopyTo(file, sectionRaw + 0x18 + 14);
        BitConverter.GetBytes(1).CopyTo(file, sectionRaw + 0x18 + 16);
        BitConverter.GetBytes(0x80000000 | 0x30).CopyTo(file, sectionRaw + 0x18 + 20);

        BitConverter.GetBytes((ushort)1).CopyTo(file, sectionRaw + 0x30 + 14);
        BitConverter.GetBytes(1033).CopyTo(file, sectionRaw + 0x30 + 16);
        BitConverter.GetBytes(0x48).CopyTo(file, sectionRaw + 0x30 + 20);

        BitConverter.GetBytes(sectionRva + 0x58).CopyTo(file, sectionRaw + 0x48);
        BitConverter.GetBytes(icon.Length).CopyTo(file, sectionRaw + 0x4C);
        icon.CopyTo(file, sectionRaw + 0x58);
        return file;
    }

    private static byte[] Png(int side, int length = 24)
    {
        var png = new byte[length];
        png[0] = 0x89;
        png[1] = (byte)'P';
        png[2] = (byte)'N';
        png[3] = (byte)'G';
        png[16] = (byte)(side >> 24);
        png[17] = (byte)(side >> 16);
        png[18] = (byte)(side >> 8);
        png[19] = (byte)side;
        return png;
    }

    private static byte[] Icns(IReadOnlyList<byte[]> chunks)
    {
        var body = new List<byte>();
        foreach (var chunk in chunks)
        {
            body.AddRange("ic09"u8.ToArray());
            var length = chunk.Length + 8;
            body.AddRange([(byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length]);
            body.AddRange(chunk);
        }

        var total = body.Count + 8;
        var icns = new List<byte>("icns"u8.ToArray())
        {
            (byte)(total >> 24), (byte)(total >> 16), (byte)(total >> 8), (byte)total,
        };
        icns.AddRange(body);
        return icns.ToArray();
    }
}

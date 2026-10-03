namespace ProxyCage.Core.Tests;

public class TrayPixmapTests
{
    private static TrayPixmap.Image Brand() =>
        TrayPixmap.Decode(File.ReadAllBytes(Path.Combine(Repo(), "assets", "cehoproxy.png")));

    private static string Repo()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "assets", "cehoproxy.png"))) dir = Path.GetDirectoryName(dir)!;
        return dir;
    }

    [Fact]
    public void Brand_picture_is_read_pixel_for_pixel()
    {
        var brand = Brand();

        Assert.Equal(256, brand.Width);
        Assert.Equal(256, brand.Height);
        Assert.Equal(0u, brand.Pixels[0]);
        Assert.Equal(0xFF192630u, brand.Pixels[128 * 256 + 128]);
        Assert.Equal(0xFF00A96Cu, brand.Pixels[45 * 256 + 180]);
    }

    [Theory]
    [InlineData(TrayLook.Protected, 0xFF2EC46Bu)]
    [InlineData(TrayLook.Stopped, 0xFF8A8F98u)]
    public void Badge_shows_the_state_in_the_corner(TrayLook look, uint centre)
    {
        var icon = TrayPixmap.Render(Brand(), look, 48);
        var side = (int)Math.Round(48 * 0.375) + 1;
        var middle = 48 - side + (side - 1) / 2;

        var pixel = icon.Pixels[middle * 48 + middle];

        Assert.Equal(centre & 0x00FFFFFF, pixel & 0x00FFFFFF);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(22)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public void Every_state_has_its_own_picture_at_each_native_size(int size)
    {
        var brand = OpticalBrand(size);
        var pictures = Enum.GetValues<TrayLook>()
            .Select(look => Convert.ToBase64String(TrayPixmap.NetworkOrder(TrayPixmap.Render(brand, look, size))))
            .ToList();

        Assert.Equal(pictures.Count, pictures.Distinct().Count());
    }

    private static TrayPixmap.Image OpticalBrand(int size) => TrayPixmap.Decode(
        File.ReadAllBytes(Path.Combine(Repo(), "assets", "tray", $"cehoproxy-tray-{size}.png")));

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(22)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public void Optical_resource_covers_each_native_size(int size)
    {
        var brand = OpticalBrand(size);
        Assert.Equal(size, brand.Width);
        Assert.Equal(size, brand.Height);
        Assert.Equal($"cehoproxy.tray.cehoproxy-tray-{size}.png", TrayPixmap.ResourceName(size));
        Assert.Equal(0u, brand.Pixels[0] >> 24);
        var icon = TrayPixmap.Render(brand, TrayLook.Protected, size);
        Assert.Equal(size * size * 4, TrayPixmap.NetworkOrder(icon).Length);
    }

    [Theory]
    [InlineData(18, 20)]
    [InlineData(25, 32)]
    [InlineData(96, 64)]
    public void Nonstandard_native_size_uses_the_next_larger_optical_resource(int size, int resource)
    {
        Assert.Equal($"cehoproxy.tray.cehoproxy-tray-{resource}.png", TrayPixmap.ResourceName(size));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Mac_bundled_templates_match_shared_renderer_and_keep_all_six_states_distinct(int scale)
    {
        var brand = TrayPixmap.Decode(File.ReadAllBytes(Path.Combine(Repo(), "assets", "tray",
            $"cehoproxy-template-{18 * scale}.png")));
        var glyphs = new List<string>();
        uint[]? firstMark = null;
        foreach (var look in Enum.GetValues<TrayLook>())
        {
            var rendered = TrayPixmap.RenderTemplate(brand, look, scale);
            var suffix = scale == 1 ? "" : "@2x";
            var asset = TrayPixmap.Decode(File.ReadAllBytes(Path.Combine(Repo(), "assets", "tray",
                $"cehoproxy-status-{look.ToString().ToLowerInvariant()}{suffix}.png")));
            Assert.Equal(26 * scale, rendered.Width);
            Assert.Equal(18 * scale, rendered.Height);
            Assert.Equal(rendered.Width, asset.Width);
            Assert.Equal(rendered.Height, asset.Height);
            Assert.Equal(rendered.Pixels, asset.Pixels);
            Assert.All(asset.Pixels, pixel => Assert.Equal(0u, pixel & 0x00FFFFFF));
            var mark = Enumerable.Range(0, rendered.Height)
                .SelectMany(y => asset.Pixels.Skip(y * rendered.Width).Take(18 * scale)).ToArray();
            if (firstMark is null) firstMark = mark;
            else Assert.Equal(firstMark, mark);
            glyphs.Add(Convert.ToBase64String(TrayPixmap.NetworkOrder(asset)));
        }
        Assert.Equal(6, glyphs.Distinct().Count());
    }

    [Theory]
    [InlineData("Windows")]
    [InlineData("Linux")]
    public void Native_project_embeds_optical_resource_family(string platform)
    {
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(Repo(), "src",
            $"ProxyCage.Tray.{platform}", $"ProxyCage.Tray.{platform}.csproj"));
        Assert.Contains(project.Descendants("EmbeddedResource"), element =>
            (string?)element.Attribute("Include") == "../../assets/tray/cehoproxy-tray-*.png" &&
            (string?)element.Attribute("LogicalName") == "cehoproxy.tray.%(Filename)%(Extension)");
    }

    [Fact]
    public void Mac_bundle_wires_template_states_and_product_application_icon()
    {
        var script = File.ReadAllText(Path.Combine(Repo(), "scripts", "build-tray-mac.sh"));
        Assert.Contains("cehoproxy-status-*.png", script);
        Assert.Contains("CFBundleIconFile", script);
        Assert.Contains("assets/cehoproxy.icns", script);
        Assert.True(File.Exists(Path.Combine(Repo(), "assets", "cehoproxy.icns")));
        var source = File.ReadAllText(Path.Combine(Repo(), "src", "ProxyCage.Tray.Mac", "CehoProxyTray.swift"));
        Assert.Contains("image.isTemplate = true", source);
        Assert.Contains("button.setAccessibilityLabel(tip)", source);
        foreach (var look in Enum.GetValues<TrayLook>())
            Assert.Contains($"state = \"{look.ToString().ToLowerInvariant()}\"", source);
    }

    [Fact]
    public void Pixels_go_out_alpha_first_as_the_tray_expects()
    {
        var image = new TrayPixmap.Image(1, 1, new[] { 0x80112233u });

        Assert.Equal(new byte[] { 0x80, 0x11, 0x22, 0x33 }, TrayPixmap.NetworkOrder(image));
    }
}

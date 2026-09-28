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
        Assert.Equal(0xFFE9FEF7u, brand.Pixels[128 * 256 + 128]);
        Assert.Equal(0xFF051E16u, brand.Pixels[200 * 256 + 10]);
    }

    [Theory]
    [InlineData(TrayLook.Protected, 0xFF2EC46Bu)]
    [InlineData(TrayLook.Stopped, 0xFF8A8F98u)]
    public void Badge_shows_the_state_in_the_corner(TrayLook look, uint centre)
    {
        var icon = TrayPixmap.Render(Brand(), look, 48);
        var side = 48 / 2 + 1;
        var middle = 48 - side + (side - 1) / 2;

        var pixel = icon.Pixels[middle * 48 + middle];

        Assert.Equal(centre & 0x00FFFFFF, pixel & 0x00FFFFFF);
    }

    [Fact]
    public void Every_state_has_its_own_picture()
    {
        var brand = Brand();
        var pictures = Enum.GetValues<TrayLook>()
            .Select(look => Convert.ToBase64String(TrayPixmap.NetworkOrder(TrayPixmap.Render(brand, look, 22))))
            .ToList();

        Assert.Equal(pictures.Count, pictures.Distinct().Count());
    }

    [Fact]
    public void Pixels_go_out_alpha_first_as_the_tray_expects()
    {
        var image = new TrayPixmap.Image(1, 1, new[] { 0x80112233u });

        Assert.Equal(new byte[] { 0x80, 0x11, 0x22, 0x33 }, TrayPixmap.NetworkOrder(image));
    }
}

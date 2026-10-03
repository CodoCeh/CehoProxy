using System.Text.Json;
using ProxyCage.Core;
var root = args[0];
var output = args[1];
Directory.CreateDirectory(output);
foreach (var size in new[] { 16, 20, 22, 24, 32, 48, 64 })
{
    var brand = TrayPixmap.Decode(File.ReadAllBytes(Path.Combine(root, "assets", "tray", $"cehoproxy-tray-{size}.png")));
    foreach (var look in Enum.GetValues<TrayLook>())
    {
        var icon = TrayPixmap.Render(brand, look, size);
        File.WriteAllText(Path.Combine(output, $"tray-{look.ToString().ToLowerInvariant()}-{size}.json"), JsonSerializer.Serialize(icon));
    }
}
foreach (var scale in new[] { 1, 2 })
{
    var brand = TrayPixmap.Decode(File.ReadAllBytes(Path.Combine(root, "assets", "tray", $"cehoproxy-template-{18 * scale}.png")));
    foreach (var look in Enum.GetValues<TrayLook>())
    {
        var icon = TrayPixmap.RenderTemplate(brand, look, scale);
        var suffix = scale == 1 ? "" : "@2x";
        File.WriteAllText(Path.Combine(output, $"cehoproxy-status-{look.ToString().ToLowerInvariant()}{suffix}.json"), JsonSerializer.Serialize(icon));
    }
}

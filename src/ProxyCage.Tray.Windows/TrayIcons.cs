using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using ProxyCage.Core;

namespace ProxyCage.Tray.Windows;

[SupportedOSPlatform("windows")]
internal static class TrayIcons
{
    private static readonly Dictionary<TrayLook, IntPtr> Cache = new();

    private static readonly Color Edge = Color.FromArgb(0xF0, 0x10, 0x14, 0x18);

    private static readonly Dictionary<TrayLook, Color> Colours = new()
    {
        [TrayLook.Protected] = Color.FromArgb(0x2E, 0xC4, 0x6B),
        [TrayLook.Starting] = Color.FromArgb(0xF5, 0xA6, 0x23),
        [TrayLook.Off] = Color.FromArgb(0xE0, 0x4F, 0x3D),
        [TrayLook.Trouble] = Color.FromArgb(0xE0, 0x4F, 0x3D),
        [TrayLook.Stopped] = Color.FromArgb(0x8A, 0x8F, 0x98),
        [TrayLook.Locked] = Color.FromArgb(0x8A, 0x8F, 0x98),
    };

    public static IntPtr Handle(TrayLook look)
    {
        if (Cache.TryGetValue(look, out var ready)) return ready;
        var handle = Compose(TrayState.Badge(look), Colours[look]);
        Cache[look] = handle;
        return handle;
    }

    public static void Release()
    {
        foreach (var handle in Cache.Values) Win32.DestroyIcon(handle);
        Cache.Clear();
    }

    private static IntPtr Compose(TrayBadge badge, Color colour)
    {
        var width = Math.Max(16, Win32.GetSystemMetrics(Win32.SM_CXSMICON));
        var height = Math.Max(16, Win32.GetSystemMetrics(Win32.SM_CYSMICON));

        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream("cehoproxy.ico")
            ?? throw new InvalidOperationException("cehoproxy.ico");
        using var brand = new Icon(stream, width, height);
        using var mark = brand.ToBitmap();
        using var bitmap = new Bitmap(width, height);
        using (var canvas = Graphics.FromImage(bitmap))
        {
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.DrawImage(mark, new Rectangle(0, 0, width, height));
            Draw(canvas, badge, colour, new Rectangle(
                width - Side(width), height - Side(width), Side(width) - 1, Side(width) - 1));
        }

        return bitmap.GetHicon();
    }

    private static int Side(int width) => Math.Max(8, width / 2 + 1);

    private static void Draw(Graphics canvas, TrayBadge badge, Color colour, Rectangle box)
    {
        using var fill = new SolidBrush(colour);
        using var back = new SolidBrush(Edge);
        using var edge = new Pen(Edge, 1f);
        using var ring = new Pen(colour, Math.Max(1.5f, box.Width / 4f));
        using var mark = new Pen(Color.White, Math.Max(1.4f, box.Width / 5f));
        using var white = new SolidBrush(Color.White);

        switch (badge)
        {
            case TrayBadge.Disc:
                canvas.FillEllipse(fill, box);
                canvas.DrawEllipse(edge, box);
                break;

            case TrayBadge.Half:
                canvas.FillEllipse(back, box);
                canvas.FillPie(fill, box, 0, 180);
                canvas.DrawEllipse(edge, box);
                break;

            case TrayBadge.Ring:
                canvas.FillEllipse(back, box);
                canvas.DrawEllipse(ring, Rectangle.Inflate(box, -1, -1));
                break;

            case TrayBadge.Slash:
                canvas.FillEllipse(fill, box);
                canvas.DrawEllipse(edge, box);
                canvas.DrawLine(mark,
                    box.Right - box.Width / 4f, box.Top + box.Height / 4f,
                    box.Left + box.Width / 4f, box.Bottom - box.Height / 4f);
                break;

            case TrayBadge.Square:
                canvas.FillRectangle(fill, box);
                canvas.DrawRectangle(edge, box);
                break;

            default:
                canvas.FillEllipse(fill, box);
                canvas.DrawEllipse(edge, box);
                var body = new RectangleF(
                    box.Left + box.Width * 0.28f, box.Top + box.Height * 0.48f,
                    box.Width * 0.44f, box.Height * 0.32f);
                canvas.FillRectangle(white, body);
                canvas.DrawArc(mark,
                    box.Left + box.Width * 0.33f, box.Top + box.Height * 0.18f,
                    box.Width * 0.34f, box.Height * 0.40f, 180, 180);
                break;
        }
    }
}

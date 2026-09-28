using System.IO.Compression;

namespace ProxyCage.Core;

public static class TrayPixmap
{
    private const uint Edge = 0xF0101418;
    private const uint White = 0xFFFFFFFF;

    private static readonly Dictionary<TrayLook, uint> Colours = new()
    {
        [TrayLook.Protected] = 0xFF2EC46B,
        [TrayLook.Starting] = 0xFFF5A623,
        [TrayLook.Off] = 0xFFE04F3D,
        [TrayLook.Trouble] = 0xFFE04F3D,
        [TrayLook.Stopped] = 0xFF8A8F98,
        [TrayLook.Locked] = 0xFF8A8F98,
    };

    public sealed record Image(int Width, int Height, uint[] Pixels);

    public static Image Decode(byte[] png)
    {
        var width = 0;
        var height = 0;
        byte colourType = 0;
        using var compressed = new MemoryStream();
        for (var at = 8; at + 8 <= png.Length;)
        {
            var length = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = at + 8;
            if (type == "IHDR")
            {
                width = BigEndian(png, data);
                height = BigEndian(png, data + 4);
                if (png[data + 8] != 8 || png[data + 12] != 0)
                    throw new InvalidDataException("png");
                colourType = png[data + 9];
            }
            else if (type == "IDAT") compressed.Write(png, data, length);
            else if (type == "IEND") break;
            at = data + length + 4;
        }

        var channels = colourType switch { 6 => 4, 2 => 3, _ => throw new InvalidDataException("png") };
        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflater.CopyTo(raw);
        var bytes = raw.ToArray();

        var stride = width * channels;
        var previous = new byte[stride];
        var line = new byte[stride];
        var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            var start = y * (stride + 1);
            var filter = bytes[start];
            for (var x = 0; x < stride; x++)
            {
                var value = bytes[start + 1 + x];
                var left = x >= channels ? line[x - channels] : 0;
                var up = previous[x];
                var corner = x >= channels ? previous[x - channels] : 0;
                line[x] = (byte)(filter switch
                {
                    1 => value + left,
                    2 => value + up,
                    3 => value + ((left + up) >> 1),
                    4 => value + Paeth(left, up, corner),
                    _ => value,
                });
            }
            for (var x = 0; x < width; x++)
            {
                var p = x * channels;
                var alpha = channels == 4 ? line[p + 3] : (byte)255;
                pixels[y * width + x] = ((uint)alpha << 24) | ((uint)line[p] << 16) | ((uint)line[p + 1] << 8) | line[p + 2];
            }
            (previous, line) = (line, previous);
        }
        return new Image(width, height, pixels);
    }

    public static Image Render(Image brand, TrayLook look, int size)
    {
        var pixels = Scale(brand, size);
        var side = Math.Max(8, size / 2 + 1);
        var box = new Box(size - side, size - side, side - 1, side - 1);
        Badge(pixels, size, TrayState.Badge(look), Colours[look], box);
        return new Image(size, size, pixels);
    }

    public static byte[] NetworkOrder(Image image)
    {
        var bytes = new byte[image.Pixels.Length * 4];
        for (var i = 0; i < image.Pixels.Length; i++)
        {
            var p = image.Pixels[i];
            bytes[i * 4] = (byte)(p >> 24);
            bytes[i * 4 + 1] = (byte)(p >> 16);
            bytes[i * 4 + 2] = (byte)(p >> 8);
            bytes[i * 4 + 3] = (byte)p;
        }
        return bytes;
    }

    private readonly record struct Box(double X, double Y, double W, double H)
    {
        public double Cx => X + W / 2;
        public double Cy => Y + H / 2;
        public double R => Math.Min(W, H) / 2;
    }

    private static void Badge(uint[] pixels, int size, TrayBadge badge, uint colour, Box box)
    {
        bool Disc(double x, double y) => Distance(x, y, box.Cx, box.Cy) <= box.R;
        var edgeWidth = 1.0;

        switch (badge)
        {
            case TrayBadge.Disc:
                Paint(pixels, size, Disc, Edge);
                Paint(pixels, size, (x, y) => Distance(x, y, box.Cx, box.Cy) <= box.R - edgeWidth, colour);
                break;

            case TrayBadge.Half:
                Paint(pixels, size, Disc, Edge);
                Paint(pixels, size, (x, y) => y >= box.Cy && Distance(x, y, box.Cx, box.Cy) <= box.R - edgeWidth, colour);
                break;

            case TrayBadge.Ring:
            {
                var thickness = Math.Max(1.5, box.W / 4);
                Paint(pixels, size, Disc, Edge);
                Paint(pixels, size, (x, y) =>
                {
                    var d = Distance(x, y, box.Cx, box.Cy);
                    return d <= box.R - 1 && d >= box.R - 1 - thickness;
                }, colour);
                break;
            }

            case TrayBadge.Slash:
            {
                var width = Math.Max(1.4, box.W / 5);
                Paint(pixels, size, Disc, Edge);
                Paint(pixels, size, (x, y) => Distance(x, y, box.Cx, box.Cy) <= box.R - edgeWidth, colour);
                Paint(pixels, size, (x, y) => Segment(x, y,
                    box.X + box.W * 0.75, box.Y + box.H * 0.25,
                    box.X + box.W * 0.25, box.Y + box.H * 0.75) <= width / 2, White);
                break;
            }

            case TrayBadge.Square:
                Paint(pixels, size, (x, y) => Inside(x, y, box.X, box.Y, box.W, box.H), Edge);
                Paint(pixels, size, (x, y) => Inside(x, y, box.X + 1, box.Y + 1, box.W - 2, box.H - 2), colour);
                break;

            default:
            {
                var width = Math.Max(1.4, box.W / 5);
                var arcCx = box.X + box.W * 0.5;
                var arcCy = box.Y + box.H * 0.38;
                var arcR = box.W * 0.17;
                Paint(pixels, size, Disc, Edge);
                Paint(pixels, size, (x, y) => Distance(x, y, box.Cx, box.Cy) <= box.R - edgeWidth, colour);
                Paint(pixels, size, (x, y) => Inside(x, y,
                    box.X + box.W * 0.28, box.Y + box.H * 0.48, box.W * 0.44, box.H * 0.32), White);
                var bodyTop = box.Y + box.H * 0.48;
                Paint(pixels, size, (x, y) =>
                    (y <= arcCy && Math.Abs(Distance(x, y, arcCx, arcCy) - arcR) <= width / 2)
                    || (y > arcCy && y <= bodyTop
                        && (Math.Abs(x - (arcCx - arcR)) <= width / 2 || Math.Abs(x - (arcCx + arcR)) <= width / 2)), White);
                break;
            }
        }
    }

    private static void Paint(uint[] pixels, int size, Func<double, double, bool> shape, uint colour)
    {
        const int samples = 4;
        for (var py = 0; py < size; py++)
        for (var px = 0; px < size; px++)
        {
            var hits = 0;
            for (var sy = 0; sy < samples; sy++)
            for (var sx = 0; sx < samples; sx++)
                if (shape(px + (sx + 0.5) / samples, py + (sy + 0.5) / samples)) hits++;
            if (hits == 0) continue;
            var cover = hits / (double)(samples * samples);
            pixels[py * size + px] = Over(pixels[py * size + px], colour, cover);
        }
    }

    private static uint Over(uint under, uint over, double cover)
    {
        var a = (over >> 24) / 255.0 * cover;
        var ua = (under >> 24) / 255.0;
        var outA = a + ua * (1 - a);
        if (outA <= 0) return 0;
        byte Mix(int shift)
        {
            var o = (over >> shift) & 0xFF;
            var u = (under >> shift) & 0xFF;
            return (byte)Math.Round((o * a + u * ua * (1 - a)) / outA);
        }
        return ((uint)Math.Round(outA * 255) << 24) | ((uint)Mix(16) << 16) | ((uint)Mix(8) << 8) | Mix(0);
    }

    private static uint[] Scale(Image source, int size)
    {
        var result = new uint[size * size];
        var step = source.Width / (double)size;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            double a = 0, r = 0, g = 0, b = 0;
            var count = 0;
            var x0 = (int)(x * step);
            var y0 = (int)(y * step);
            var x1 = Math.Max(x0 + 1, (int)((x + 1) * step));
            var y1 = Math.Max(y0 + 1, (int)((y + 1) * step));
            for (var sy = y0; sy < y1 && sy < source.Height; sy++)
            for (var sx = x0; sx < x1 && sx < source.Width; sx++)
            {
                var p = source.Pixels[sy * source.Width + sx];
                var pa = (p >> 24) / 255.0;
                a += pa;
                r += ((p >> 16) & 0xFF) * pa;
                g += ((p >> 8) & 0xFF) * pa;
                b += (p & 0xFF) * pa;
                count++;
            }
            if (count == 0 || a <= 0) continue;
            result[y * size + x] = ((uint)Math.Round(a / count * 255) << 24)
                | ((uint)Math.Round(r / a) << 16) | ((uint)Math.Round(g / a) << 8) | (uint)Math.Round(b / a);
        }
        return result;
    }

    private static double Distance(double x, double y, double cx, double cy) =>
        Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

    private static bool Inside(double x, double y, double left, double top, double w, double h) =>
        x >= left && x <= left + w && y >= top && y <= top + h;

    private static double Segment(double x, double y, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy), 0, 1);
        return Distance(x, y, ax + t * dx, ay + t * dy);
    }

    private static int BigEndian(byte[] data, int at) =>
        (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}

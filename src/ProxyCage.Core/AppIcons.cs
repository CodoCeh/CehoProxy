using System.Text.RegularExpressions;

namespace ProxyCage.Core;

public static class AppIcons
{
    public sealed record Icon(byte[] Bytes, string ContentType);

    private const int Preferred = 96;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Icon?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Icon? Load(string path, string lang = "ru")
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (Gate)
            if (Cache.TryGetValue(path, out var cached)) return cached;

        Icon? icon;
        try
        {
            icon = Os.Kind switch
            {
                OsKind.Mac => FromBundle(path),
                OsKind.Windows => FromExecutable(path),
                _ => FromDesktop(path, lang),
            };
        }
        catch { icon = null; }

        lock (Gate)
        {
            if (Cache.Count > 600) Cache.Clear();
            Cache[path] = icon;
        }
        return icon;
    }

    private static Icon? FromBundle(string path)
    {
        var bundle = BundleRoot(path);
        if (bundle is null) return null;
        var resources = Path.Combine(bundle, "Contents", "Resources");
        if (!Directory.Exists(resources)) return null;

        var files = Directory.EnumerateFiles(resources, "*.icns", SearchOption.TopDirectoryOnly).ToList();
        if (files.Count == 0) return null;

        var file = Named(files, PlistIconFile(Path.Combine(bundle, "Contents", "Info.plist")))
                   ?? Named(files, "AppIcon")
                   ?? Named(files, Path.GetFileNameWithoutExtension(bundle))
                   ?? files.OrderByDescending(f => new FileInfo(f).Length).First();

        var png = IcnsPng(File.ReadAllBytes(file));
        return png is null ? null : new Icon(png, "image/png");
    }

    internal static string? BundleRoot(string path)
    {
        var dir = path.TrimEnd('/');
        while (dir.Length > 1)
        {
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir) return null;
            dir = parent;
        }
        return null;
    }

    private static string? Named(List<string> files, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                .Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase));

    private static string? PlistIconFile(string plist)
    {
        if (!File.Exists(plist)) return null;
        string text;
        try { text = File.ReadAllText(plist); }
        catch { return null; }
        var match = Regex.Match(text, "<key>CFBundleIconFile</key>\\s*<string>(?<name>[^<]+)</string>");
        return match.Success ? match.Groups["name"].Value.Trim() : null;
    }

    internal static byte[]? IcnsPng(byte[] data, int preferred = Preferred)
    {
        if (data.Length < 8 || data[0] != (byte)'i' || data[1] != (byte)'c'
            || data[2] != (byte)'n' || data[3] != (byte)'s') return null;

        byte[]? best = null;
        var bestSide = 0;
        var offset = 8;
        while (offset + 8 <= data.Length)
        {
            var length = BigEndian(data, offset + 4);
            if (length < 8 || length > data.Length - offset) break;
            var start = offset + 8;
            var side = PngSide(data, start, length - 8);
            if (side > 0 && Closer(side, bestSide, preferred))
            {
                best = data[start..(offset + length)];
                bestSide = side;
            }
            offset += length;
        }
        return best;
    }

    private static bool Closer(int side, int bestSide, int preferred)
    {
        if (bestSide == 0) return true;
        if (bestSide < preferred) return side > bestSide;
        return side >= preferred && side < bestSide;
    }

    private static int PngSide(byte[] data, int start, int length)
    {
        if (length < 24 || start + 24 > data.Length) return 0;
        if (data[start] != 0x89 || data[start + 1] != (byte)'P'
            || data[start + 2] != (byte)'N' || data[start + 3] != (byte)'G') return 0;
        return BigEndian(data, start + 16);
    }

    private static int BigEndian(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    internal static Icon? FromExecutable(string path)
    {
        if (!File.Exists(path)) return null;
        if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            return new Icon(File.ReadAllBytes(path), "image/x-icon");

        using var file = new PeFile(path);
        return file.Icon();
    }

    private sealed class PeFile : IDisposable
    {
        private readonly FileStream _stream;
        private readonly byte[] _word = new byte[4];

        public PeFile(string path) => _stream = File.OpenRead(path);

        public void Dispose() => _stream.Dispose();

        public Icon? Icon()
        {
            if (U16(0) != 0x5A4D) return null;
            long pe = U32(0x3C);
            if (pe <= 0 || pe + 24 > _stream.Length || U32(pe) != 0x00004550) return null;

            var sections = U16(pe + 6);
            var optionalSize = U16(pe + 20);
            var optional = pe + 24;
            var directories = optional + (U16(optional) == 0x20B ? 112 : 96);
            var resourceRva = U32(directories + 16);
            if (resourceRva == 0) return null;

            var table = new List<(uint Rva, uint Size, uint Raw)>();
            for (var i = 0; i < sections; i++)
            {
                var head = pe + 24 + optionalSize + 40L * i;
                if (head + 40 > _stream.Length) break;
                var virtualSize = U32(head + 8);
                var rawSize = U32(head + 16);
                table.Add((U32(head + 12), Math.Max(virtualSize, rawSize), U32(head + 20)));
            }

            long Offset(uint rva)
            {
                foreach (var (start, size, raw) in table)
                    if (rva >= start && rva < start + size) return raw + (rva - start);
                return -1;
            }

            var root = Offset(resourceRva);
            if (root < 0) return null;

            var icons = IconLeaves(root);
            if (icons.Count == 0) return null;

            byte[]? bestPng = null;
            byte[]? bestDib = null;
            var bestSide = 0;
            foreach (var leaf in icons)
            {
                var at = Offset(U32(leaf));
                var size = (int)U32(leaf + 4);
                if (at < 0 || size <= 24 || at + size > _stream.Length) continue;

                var head = Read(at, 40);
                if (head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G')
                {
                    var side = BigEndian(head, 16);
                    if (!Closer(side, bestSide, Preferred)) continue;
                    bestPng = Read(at, size);
                    bestDib = null;
                    bestSide = side;
                    continue;
                }

                if (BitConverter.ToUInt32(head, 0) != 40) continue;
                var width = BitConverter.ToInt32(head, 4);
                if (width <= 0 || !Closer(width, bestSide, Preferred)) continue;
                bestDib = Read(at, size);
                bestPng = null;
                bestSide = width;
            }

            if (bestPng is not null) return new Icon(bestPng, "image/png");
            return bestDib is null ? null : new Icon(IcoFromDib(bestDib), "image/x-icon");
        }

        private List<long> IconLeaves(long root)
        {
            var empty = new List<long>();
            if (root + 16 > _stream.Length) return empty;
            var count = U16(root + 12) + U16(root + 14);
            for (var i = 0; i < count; i++)
            {
                var entry = root + 16 + 8L * i;
                if (entry + 8 > _stream.Length) return empty;
                if (U32(entry) != 3) continue;
                var child = U32(entry + 4);
                if ((child & 0x80000000) == 0) return empty;
                return Leaves(root, root + (child & 0x7FFFFFFF), 0);
            }
            return empty;
        }

        private List<long> Leaves(long root, long dir, int depth)
        {
            var found = new List<long>();
            if (depth > 3 || dir + 16 > _stream.Length) return found;
            var count = U16(dir + 12) + U16(dir + 14);
            for (var i = 0; i < count; i++)
            {
                var entry = dir + 16 + 8L * i;
                if (entry + 8 > _stream.Length) break;
                var child = U32(entry + 4);
                if ((child & 0x80000000) != 0)
                    found.AddRange(Leaves(root, root + (child & 0x7FFFFFFF), depth + 1));
                else
                {
                    found.Add(root + child);
                    break;
                }
                if (found.Count > 64) break;
            }
            return found;
        }

        private byte[] Read(long offset, int count)
        {
            var buffer = new byte[count];
            _stream.Position = offset;
            _stream.ReadExactly(buffer, 0, count);
            return buffer;
        }

        private uint U32(long offset)
        {
            _stream.Position = offset;
            _stream.ReadExactly(_word, 0, 4);
            return BitConverter.ToUInt32(_word, 0);
        }

        private ushort U16(long offset)
        {
            _stream.Position = offset;
            _stream.ReadExactly(_word, 0, 2);
            return BitConverter.ToUInt16(_word, 0);
        }
    }

    internal static byte[] IcoFromDib(byte[] dib)
    {
        var width = BitConverter.ToInt32(dib, 4);
        var height = BitConverter.ToInt32(dib, 8) / 2;
        var bits = BitConverter.ToUInt16(dib, 14);
        var ico = new byte[22 + dib.Length];
        ico[2] = 1;
        ico[4] = 1;
        ico[6] = (byte)(width >= 256 ? 0 : width);
        ico[7] = (byte)(height >= 256 ? 0 : height);
        ico[10] = 1;
        BitConverter.GetBytes(bits).CopyTo(ico, 12);
        BitConverter.GetBytes(dib.Length).CopyTo(ico, 14);
        BitConverter.GetBytes(22).CopyTo(ico, 18);
        dib.CopyTo(ico, 22);
        return ico;
    }

    private static Icon? FromDesktop(string path, string lang)
    {
        string? name;
        try
        {
            name = InstalledAppCatalog.Detect(lang)
                .FirstOrDefault(e => e.Path.Equals(path, StringComparison.Ordinal))?.Icon;
        }
        catch { return null; }

        var file = LinuxIconFile(name);
        if (file is null) return null;
        var type = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/png",
        };
        return new Icon(File.ReadAllBytes(file), type);
    }

    internal static string? LinuxIconFile(string? name, Func<string, bool>? exists = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        exists ??= File.Exists;
        if (Path.IsPathRooted(name)) return exists(name) ? name : null;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots =
        [
            Path.Combine(home, ".local/share/icons"),
            "/usr/share/icons",
            "/usr/local/share/icons",
            Path.Combine(home, ".local/share/flatpak/exports/share/icons"),
            "/var/lib/flatpak/exports/share/icons",
        ];
        string[] themes = ["hicolor", "Adwaita", "gnome", "breeze", "Papirus"];
        string[] sizes = ["128x128", "96x96", "64x64", "256x256", "48x48", "512x512", "scalable"];
        string[] extensions = [".png", ".svg"];

        foreach (var root in roots)
            foreach (var theme in themes)
                foreach (var size in sizes)
                    foreach (var extension in extensions)
                    {
                        var file = Path.Combine(root, theme, size, "apps", name + extension);
                        if (exists(file)) return file;
                    }

        foreach (var pixmaps in new[] { "/usr/share/pixmaps", Path.Combine(home, ".local/share/pixmaps") })
            foreach (var extension in extensions)
            {
                var file = Path.Combine(pixmaps, name + extension);
                if (exists(file)) return file;
            }

        return null;
    }
}

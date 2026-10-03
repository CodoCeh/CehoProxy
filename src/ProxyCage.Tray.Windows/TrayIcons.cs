using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ProxyCage.Core;

namespace ProxyCage.Tray.Windows;

[SupportedOSPlatform("windows")]
internal static class TrayIcons
{
    private static readonly Dictionary<(TrayLook Look, int Size), IntPtr> Cache = new();

    public static IntPtr Handle(TrayLook look)
    {
        var size = Math.Max(16, Math.Max(
            Win32.GetSystemMetrics(Win32.SM_CXSMICON),
            Win32.GetSystemMetrics(Win32.SM_CYSMICON)));
        if (Cache.TryGetValue((look, size), out var ready)) return ready;
        var handle = Compose(look, size);
        Cache[(look, size)] = handle;
        return handle;
    }

    public static void Release()
    {
        foreach (var handle in Cache.Values) Win32.DestroyIcon(handle);
        Cache.Clear();
    }

    private static IntPtr Compose(TrayLook look, int size)
    {
        // Keep the native HICON pixels identical to the tested Linux pixmaps.
        // Optical tray assets use a high-contrast tile for both taskbar themes.
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(TrayPixmap.ResourceName(size))
            ?? throw new InvalidOperationException(TrayPixmap.ResourceName(size));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var image = TrayPixmap.Render(TrayPixmap.Decode(buffer.ToArray()), look, size);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++) row[x] = unchecked((int)image.Pixels[y * size + x]);
                Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), size);
            }
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap.GetHicon();
    }
}

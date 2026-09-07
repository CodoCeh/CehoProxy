using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>
/// Постоянный идентификатор этой установки. Панели вроде Remnawave/Happ
/// без заголовка x-hwid отдают заглушку «включите отправку HWID», а не ноды.
/// </summary>
public static class DeviceStamp
{
    private static readonly Regex Valid = new("^[a-zA-Z0-9=-]{10,64}$", RegexOptions.CultureInvariant);

    public static string FilePath(string root) => Path.Combine(root, "hwid.txt");

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Valid.IsMatch(value.Trim());

    public static string LoadOrCreate(string root)
    {
        var path = FilePath(root);
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (IsValid(existing)) return existing;
            }
        }
        catch { }

        var id = "ceho" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, id);
        }
        catch { }

        return id;
    }
}

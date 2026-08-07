using System.Security.Cryptography;
using System.Text;

namespace ProxyCage.Core;

/// <summary>
/// Пароль на панель и команды.
///
/// Зачем он вообще нужен, если панель слушает только петлю: на терминальном сервере
/// в системе одновременно работают РАЗНЫЕ люди, и петля доступна каждому из них.
/// Без пароля любой вошедший мог бы менять правила изоляции и подписки.
/// Сам туннель при этом один на машину — так и задумано, см. README.
/// </summary>
public static class Auth
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    public static bool HasPassword(CehoConfig cfg) =>
        !string.IsNullOrEmpty(cfg.PasswordHash) && !string.IsNullOrEmpty(cfg.PasswordSalt);

    public static void SetPassword(CehoConfig cfg, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        cfg.PasswordSalt = Convert.ToBase64String(salt);
        cfg.PasswordHash = Convert.ToBase64String(Derive(password, salt));
    }

    public static void ClearPassword(CehoConfig cfg)
    {
        cfg.PasswordHash = null;
        cfg.PasswordSalt = null;
    }

    public static bool Verify(CehoConfig cfg, string? password)
    {
        if (!HasPassword(cfg)) return true;
        if (string.IsNullOrEmpty(password)) return false;

        try
        {
            var salt = Convert.FromBase64String(cfg.PasswordSalt!);
            var expected = Convert.FromBase64String(cfg.PasswordHash!);
            // сравнение постоянного времени: обычное «==» подсказывает длину общего префикса
            return CryptographicOperations.FixedTimeEquals(Derive(password, salt), expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);

    // ── сессии панели ────────────────────────────────────────────────

    private static readonly Dictionary<string, DateTime> Sessions = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static readonly TimeSpan SessionLife = TimeSpan.FromHours(12);

    public static string IssueSession()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (Gate)
        {
            Sweep();
            Sessions[token] = DateTime.UtcNow + SessionLife;
        }
        return token;
    }

    public static bool ValidSession(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (Gate)
        {
            Sweep();
            return Sessions.ContainsKey(token);
        }
    }

    /// <summary>Смена пароля разлогинивает всех: старая вкладка не должна пережить смену.</summary>
    public static void DropAllSessions()
    {
        lock (Gate) Sessions.Clear();
    }

    private static void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var dead in Sessions.Where(s => s.Value < now).Select(s => s.Key).ToList())
            Sessions.Remove(dead);
    }

    /// <summary>
    /// Закрывает СОДЕРЖИМОЕ настроек от посторонних: в них ссылки на подписки, а это доступ
    /// к чужому VPN. Саму папку оставляем читаемой — внутри лежит указатель на порт панели,
    /// и обычный пользователь на общем сервере должен уметь узнать, куда ему идти.
    /// </summary>
    public static void RestrictConfigAccess(string path)
    {
        if (Os.IsWindows) return;   // папка в ProgramData и так закрыта правами системы
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Os.Run("chmod", $"755 {dir}", 5000);
            if (File.Exists(path)) Os.Run("chmod", $"600 {path}", 5000);
            if (!string.IsNullOrEmpty(dir))
                foreach (var cache in Directory.GetFiles(dir, "sub-*.txt"))
                    Os.Run("chmod", $"600 {cache}", 5000);
        }
        catch { }
    }

    /// <summary>
    /// Публичный указатель на панель: одна строка с номером порта, читают все.
    /// Секрета в нём нет, а без него человек без прав не знает, куда подключаться.
    /// </summary>
    public static void WritePanelPointer(string root, int panelPort, int proxyPort)
    {
        try
        {
            Directory.CreateDirectory(root);
            var file = Path.Combine(root, "panel.port");
            File.WriteAllText(file, $"{panelPort} {proxyPort}");
            if (!Os.IsWindows) Os.Run("chmod", $"644 {file}", 5000);
        }
        catch { }
    }

    public static int? ReadPanelPointer(string root) => ReadPointer(root, 0);

    /// <summary>Порт прокси нужен и тем, у кого нет прав на настройки: «chp run» — их команда.</summary>
    public static int? ReadProxyPointer(string root) => ReadPointer(root, 1);

    private static int? ReadPointer(string root, int index)
    {
        try
        {
            var file = Path.Combine(root, "panel.port");
            if (!File.Exists(file)) return null;
            var parts = File.ReadAllText(file).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > index && int.TryParse(parts[index], out var p) ? p : null;
        }
        catch
        {
            return null;
        }
    }
}

namespace ProxyCage.Core;

/// <summary>
/// Настройки, оставленные при удалении «с сохранением»: подписки, программы, правила.
/// Лежат в папке программы, чтобы новая установка нашла их и предложила вернуть.
/// </summary>
public static class SavedSettings
{
    public const string FolderName = "saved-settings";

    public static string Dir(string root) => Path.Combine(root, FolderName);

    public static bool Exists(string root) => File.Exists(Path.Combine(Dir(root), "config.json"));

    public static void Save(string root)
    {
        var config = Path.Combine(root, "config.json");
        if (!File.Exists(config)) return;
        var dir = Dir(root);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        File.Copy(config, Path.Combine(dir, "config.json"));
        foreach (var file in Directory.GetFiles(root, "sub-*.txt"))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        Auth.RestrictConfigAccess(Path.Combine(dir, "config.json"));
    }

    public static (int Subscriptions, int Apps)? Summary(string root)
    {
        if (!Exists(root)) return null;
        try
        {
            var cfg = CehoConfig.Load(Path.Combine(Dir(root), "config.json"));
            return (cfg.Subscriptions.Count, cfg.Apps.Count);
        }
        catch { return null; }
    }

    public static void Restore(string root)
    {
        var dir = Dir(root);
        if (!Exists(root)) return;
        File.Copy(Path.Combine(dir, "config.json"), Path.Combine(root, "config.json"), overwrite: true);
        Auth.RestrictConfigAccess(Path.Combine(root, "config.json"));
        foreach (var file in Directory.GetFiles(dir, "sub-*.txt"))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)), overwrite: true);
        Discard(root);
    }

    public static void Discard(string root)
    {
        try { if (Directory.Exists(Dir(root))) Directory.Delete(Dir(root), recursive: true); } catch { }
    }

    /// <summary>Папка программы — только наша: удаляем целиком, лишь если имя совпадает с нашим.</summary>
    public static bool IsOwnRoot(string root) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Equals("CehoProxy", StringComparison.OrdinalIgnoreCase);
}

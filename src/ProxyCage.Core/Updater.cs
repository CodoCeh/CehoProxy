using System.Text.Json;

namespace ProxyCage.Core;

/// <summary>
/// Обновление из релизов GitHub: проверить, скачать, заменить себя, перезапустить службу.
///
/// Движок sing-box сюда не входит и не обновляется: он лицензирован отдельно и ставится
/// пользователем, см. THIRD-PARTY.md. Обновляется только сам CehoProxy.
/// </summary>
public static class Updater
{
    public sealed record Release(string Version, string Url, long Size, string? Notes);

    /// <summary>
    /// Версия работающей программы. Берём сборку, с которой её запустили, а не свою:
    /// номер стоит на программе, и читать его у библиотеки — значит однажды показать
    /// человеку не ту версию и не заметить вышедшее обновление.
    /// </summary>
    public static string CurrentVersion =>
        (System.Reflection.Assembly.GetEntryAssembly() ?? typeof(Updater).Assembly)
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? (System.Reflection.Assembly.GetEntryAssembly() ?? typeof(Updater).Assembly).GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>Имя файла в релизе для текущей системы и процессора.</summary>
    public static string AssetName()
    {
        var os = Os.Kind switch
        {
            OsKind.Windows => "win",
            OsKind.Mac => "osx",
            _ => "linux",
        };
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => "x64",
        };
        return $"cehoproxy-{os}-{arch}" + (Os.IsWindows ? ".exe" : "");
    }

    /// <summary>null — обновлений нет. Ошибку сети пробрасываем: молчать про неё нельзя.</summary>
    public static async Task<Release?> CheckAsync(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo))
            throw new InvalidOperationException("Не задан репозиторий обновлений.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CehoProxy");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        var response = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                $"Репозиторий обновлений «{repo}» не найден или в нём ещё нет релизов. " +
                "Проверьте имя: chp update --repo владелец/имя");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
        if (tag.Length == 0 || !IsNewer(tag, CurrentVersion)) return null;

        var wanted = AssetName();
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
            return new Release(
                tag,
                asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.GetProperty("size").GetInt64(),
                root.TryGetProperty("body", out var body) ? body.GetString() : null);
        }

        throw new InvalidOperationException($"В релизе {tag} нет файла {wanted} для этой системы.");
    }

    private static bool IsNewer(string candidate, string current)
    {
        static int[] Parts(string v) =>
            v.Split('.', '-')
             .Select(p => int.TryParse(p, out var n) ? n : 0)
             .Concat(new[] { 0, 0, 0 }).Take(3).ToArray();

        var a = Parts(candidate);
        var b = Parts(current);
        for (var i = 0; i < 3; i++)
        {
            if (a[i] > b[i]) return true;
            if (a[i] < b[i]) return false;
        }
        return false;
    }

    /// <summary>
    /// Скачивает и ставит на место себя. Работающий файл заменяем через переименование:
    /// на Windows запущенный .exe перезаписать нельзя, но переименовать можно, и новый
    /// встанет на его место. На Unix rename поверх тоже безопасен — открытый файл живёт по inode.
    /// </summary>
    public static async Task<string> InstallAsync(Release release, string targetPath, Action<string>? log = null)
    {
        var temp = targetPath + ".new";
        var backup = targetPath + ".old";

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CehoProxy");
            log?.Invoke($"скачиваю {release.Version} ({release.Size / 1024 / 1024} МБ)");

            await using var stream = await http.GetStreamAsync(release.Url);
            await using var file = File.Create(temp);
            await stream.CopyToAsync(file);
        }

        if (new FileInfo(temp).Length < 1024 * 1024)
        {
            File.Delete(temp);
            throw new InvalidOperationException("Скачанный файл слишком мал — похоже, это не программа.");
        }

        if (!Os.IsWindows) Os.Run("chmod", $"755 {temp}", 5000);

        try { if (File.Exists(backup)) File.Delete(backup); } catch { }
        if (File.Exists(targetPath)) File.Move(targetPath, backup, overwrite: true);
        File.Move(temp, targetPath, overwrite: true);

        log?.Invoke($"установлено: {release.Version}");
        return release.Version;
    }
}

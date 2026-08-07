namespace ProxyCage.Core;

/// <summary>
/// Установка и полное удаление продукта своими силами, без сторонних инсталляторов.
///
/// Нужно потому, что поставка идёт двумя путями: файлом из релиза GitHub и обычным
/// установщиком. Логика в обоих случаях одна и та же, и держать её в двух местах нельзя —
/// разъедется. Установщик под Windows просто вызывает эту команду.
///
/// Движок sing-box мы не раздаём (он под GPL, см. THIRD-PARTY.md), но скачать его с сайта
/// автора по просьбе пользователя — обычное действие пользователя, а не раздача с нашей стороны.
/// </summary>
public static class Installer
{
    public static string BinaryPath(string root) =>
        Path.Combine(root, Os.IsWindows ? "cehoproxy.exe" : "cehoproxy");

    /// <summary>Копирует программу в папку продукта, делает короткую команду и правит PATH.</summary>
    public static string Install(string root, Action<string> log)
    {
        Directory.CreateDirectory(root);

        var target = BinaryPath(root);
        var self = Environment.ProcessPath
                   ?? throw new InvalidOperationException("не удалось определить путь к программе");

        if (!Os.RealPath(self).Equals(Os.RealPath(target), StringComparison.OrdinalIgnoreCase))
        {
            // работающий файл нельзя перезаписать, но можно отодвинуть
            var backup = target + ".old";
            try { if (File.Exists(backup)) File.Delete(backup); } catch { }
            if (File.Exists(target)) File.Move(target, backup, overwrite: true);
            File.Copy(self, target, overwrite: true);
            log($"программа: {target}");
        }

        if (!Os.IsWindows) Os.Run("chmod", $"755 {target}", 5000);

        MakeShortcut(root, target, log);
        AddToPath(root, log);
        return target;
    }

    private static void MakeShortcut(string root, string target, Action<string> log)
    {
        try
        {
            if (Os.IsWindows)
            {
                var cmd = Path.Combine(root, "chp.cmd");
                File.WriteAllText(cmd, "@echo off\r\n\"" + target + "\" %*\r\n");
                return;
            }

            var link = Path.Combine(Path.GetDirectoryName(target)!, "chp");
            if (File.Exists(link)) File.Delete(link);
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex)
        {
            log($"короткую команду создать не удалось: {ex.Message}");
        }
    }

    /// <summary>
    /// Без записи в PATH человек вынужден каждый раз печатать полный путь,
    /// а команда chp из панели и из документации просто не работает.
    /// </summary>
    private static void AddToPath(string root, Action<string> log)
    {
        if (Os.IsWindows)
        {
            var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            if (current.Split(';').Any(p => p.Trim().TrimEnd('\\')
                    .Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                return;
            try
            {
                Environment.SetEnvironmentVariable("Path",
                    current.TrimEnd(';') + ";" + root, EnvironmentVariableTarget.Machine);
                log("Дальше всё делается командой  chp");
                log("В уже открытых окнах терминала она появится после их перезапуска.");
            }
            catch (Exception ex)
            {
                log($"PATH изменить не удалось: {ex.Message}");
            }
            return;
        }

        // на Unix кладём ссылку туда, что и так есть в PATH у всех
        try
        {
            var link = "/usr/local/bin/chp";
            if (File.Exists(link)) File.Delete(link);
            File.CreateSymbolicLink(link, BinaryPath(root));
            log("Дальше всё делается командой  chp");
        }
        catch (Exception ex)
        {
            log($"короткую команду chp создать не удалось: {ex.Message}");
            log($"тогда вызывайте программу полным путём: {BinaryPath(root)}");
        }
    }

    /// <summary>Убирает всё, что положил Install. Файлы настроек чистит команда uninstall.</summary>
    public static void Remove(string root, Action<string> log)
    {
        if (Os.IsWindows)
        {
            try
            {
                var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
                var cleaned = string.Join(';', current.Split(';')
                    .Where(p => !p.Trim().TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.Length > 0));
                if (cleaned != current)
                {
                    Environment.SetEnvironmentVariable("Path", cleaned, EnvironmentVariableTarget.Machine);
                    log("папка убрана из PATH");
                }
            }
            catch (Exception ex)
            {
                log($"PATH почистить не удалось: {ex.Message}");
            }
        }
        else
        {
            foreach (var link in new[] { "/usr/local/bin/chp", "/usr/local/bin/cehoproxy" })
                try { if (File.Exists(link)) File.Delete(link); } catch { }
        }

        foreach (var name in new[] { "chp.cmd", "chp" })
            try
            {
                var f = Path.Combine(root, name);
                if (File.Exists(f)) File.Delete(f);
            }
            catch { }
    }

    /// <summary>
    /// Скачивает движок с сайта автора в папку продукта. Раздачей с нашей стороны это не является:
    /// файл берётся напрямую из релизов sing-box по явной просьбе пользователя.
    /// </summary>
    public static async Task<string> DownloadEngineAsync(string root, Action<string> log)
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "amd64";
        var os = Os.Kind switch { OsKind.Windows => "windows", OsKind.Mac => "darwin", _ => "linux" };

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CehoProxy");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        var json = await http.GetStringAsync("https://api.github.com/repos/SagerNet/sing-box/releases/latest");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";

        var wanted = $"sing-box-{tag}-{os}-{arch}." + (Os.IsWindows ? "zip" : "tar.gz");
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "")
                .Equals(wanted, StringComparison.OrdinalIgnoreCase));

        if (asset.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new InvalidOperationException($"в релизе sing-box {tag} нет файла {wanted}");

        var url = asset.GetProperty("browser_download_url").GetString()!;
        log($"скачиваю движок sing-box {tag}");

        var archive = Path.Combine(root, wanted);
        await using (var stream = await http.GetStreamAsync(url))
        await using (var file = File.Create(archive))
            await stream.CopyToAsync(file);

        var engine = Path.Combine(root, Os.SingBoxFileName);
        Extract(archive, root, engine);
        try { File.Delete(archive); } catch { }

        if (!File.Exists(engine))
            throw new InvalidOperationException("движок скачался, но распаковать его не удалось");

        if (!Os.IsWindows) Os.Run("chmod", $"755 {engine}", 5000);
        log($"движок: {engine}");
        return engine;
    }

    /// <summary>Внутри архива движок лежит в подпапке с версией — достаём только его.</summary>
    private static void Extract(string archive, string root, string engine)
    {
        var temp = Path.Combine(root, "engine-tmp");
        try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        Directory.CreateDirectory(temp);

        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, temp, overwriteFiles: true);
        else
            Os.Run("tar", $"-xzf \"{archive}\" -C \"{temp}\"", 120000);

        var found = Directory.EnumerateFiles(temp, Os.SingBoxFileName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (found is not null) File.Copy(found, engine, overwrite: true);

        try { Directory.Delete(temp, true); } catch { }
    }
}

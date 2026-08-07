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
    public static string Install(string root, Action<string> log, string lang = "ru")
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
            log(Strings.T(lang, "inst_binary_at", target));
        }

        if (!Os.IsWindows) Os.Run("chmod", $"755 {target}", 5000);

        MakeShortcut(root, target, log, lang);
        AddToPath(root, log, lang);
        return target;
    }

    private static void MakeShortcut(string root, string target, Action<string> log, string lang)
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
            log(Strings.T(lang, "inst_alias_failed", ex.Message));
        }
    }

    /// <summary>
    /// Без записи в PATH человек вынужден каждый раз печатать полный путь,
    /// а команда chp из панели и из документации просто не работает.
    /// </summary>
    private static void AddToPath(string root, Action<string> log, string lang)
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
                log(Strings.T(lang, "inst_alias_ok"));
                log(Strings.T(lang, "inst_alias_reopen"));
            }
            catch (Exception ex)
            {
                log(Strings.T(lang, "inst_path_failed", ex.Message));
            }
            return;
        }

        // на Unix кладём ссылку туда, что и так есть в PATH у всех
        try
        {
            var link = "/usr/local/bin/chp";
            if (File.Exists(link)) File.Delete(link);
            File.CreateSymbolicLink(link, BinaryPath(root));
            log(Strings.T(lang, "inst_alias_ok"));
        }
        catch (Exception ex)
        {
            log(Strings.T(lang, "inst_alias_failed", ex.Message));
            log(Strings.T(lang, "inst_alias_fallback", BinaryPath(root)));
        }
    }

    /// <summary>Убирает всё, что положил Install. Файлы настроек чистит команда uninstall.</summary>
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? newName, int flags);

    private const int DelayUntilReboot = 0x4;

    /// <summary>
    /// Просит Windows удалить файл при следующей перезагрузке.
    ///
    /// Нужно ровно для одного: деинсталлятор не может стереть сам себя, пока работает,
    /// и после «полного удаления» в папке навсегда оставался четырёхмегабайтный файл.
    /// Поймано живьём. Своими файлами это не занимается — их мы удаляем сразу.
    /// </summary>
    private static void DeleteAtReboot(string path, Action<string> log, string lang)
    {
        try
        {
            if (MoveFileEx(path, null, DelayUntilReboot))
                log(Strings.T(lang, "inst_rm_reboot", Path.GetFileName(path)));
        }
        catch { }
    }

    public static void Remove(string root, Action<string> log, string lang = "ru")
    {
        if (Os.IsWindows)
        {
            // остатки установщика: сам себя он стереть не может
            try
            {
                foreach (var leftover in Directory.GetFiles(root, "unins*.*"))
                    DeleteAtReboot(leftover, log, lang);
                DeleteAtReboot(root, log, lang);
            }
            catch { }

            try
            {
                var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
                var cleaned = string.Join(';', current.Split(';')
                    .Where(p => !p.Trim().TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.Length > 0));
                if (cleaned != current)
                {
                    Environment.SetEnvironmentVariable("Path", cleaned, EnvironmentVariableTarget.Machine);
                    log(Strings.T(lang, "inst_path_cleaned"));
                }
            }
            catch (Exception ex)
            {
                log(Strings.T(lang, "inst_path_clean_failed", ex.Message));
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
    public static async Task<string> DownloadEngineAsync(string root, Action<string> log, string lang = "ru")
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
        log(Strings.T(lang, "inst_engine_downloading", tag));

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
        log(Strings.T(lang, "inst_engine_at", engine));
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

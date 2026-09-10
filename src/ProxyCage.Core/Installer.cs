namespace ProxyCage.Core;

public static class Installer
{
    public static string BinaryPath(string root) =>
        Path.Combine(root, Os.IsWindows ? "cehoproxy.exe" : "cehoproxy");

    public const string CronetFileName = "libcronet.dll";

    /// <summary>Naive outbound on Windows needs libcronet.dll next to ceho-engine.exe.</summary>
    public static bool MissingCronetDll(string root) =>
        Os.IsWindows
        && File.Exists(Path.Combine(root, Os.EngineFileName))
        && !File.Exists(Path.Combine(root, CronetFileName));

    public static void CopyCronetDependencies(string sourceDir, string root)
    {
        if (!Os.IsWindows || !Directory.Exists(sourceDir)) return;
        foreach (var dll in Directory.EnumerateFiles(sourceDir, "libcronet.*", SearchOption.TopDirectoryOnly))
        {
            var dest = Path.Combine(root, Path.GetFileName(dll));
            // install.ps1 кладёт DLL в ту же папку, откуда потом вызывается
            // `cehoproxy install`. CopyFile на самого себя на Windows даёт
            // ERROR_SHARING_VIOLATION («файл занят другим процессом»).
            if (Os.RealPath(dll).Equals(Os.RealPath(dest), StringComparison.OrdinalIgnoreCase))
                continue;

            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    File.Copy(dll, dest, overwrite: true);
                    break;
                }
                catch (IOException) when (File.Exists(dest))
                {
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(400);
                }
            }
        }
    }

    /// <summary>Naive outbound на Windows: докачать sing-box, если рядом с ceho-engine.exe нет libcronet.dll.</summary>
    public static async Task EnsureCronetAsync(
        string root, Action<string> log, string lang = "ru", CancellationToken cancel = default)
    {
        if (!MissingCronetDll(root)) return;
        log(Strings.T(lang, "engine_cronet_missing", root));

        var targetDll = Path.Combine(root, CronetFileName);
        try
        {
            using var http = DirectHttp.CreateClient(TimeSpan.FromSeconds(30));
            var directUrl = "https://github.com/CodoCeh/CehoProxy/releases/latest/download/libcronet.dll";
            var tempDll = Path.Combine(root, CronetFileName + ".dl");
            await using (var stream = await http.GetStreamAsync(directUrl, cancel))
            await using (var file = File.Create(tempDll))
                await stream.CopyToAsync(file, cancel);
            if (new FileInfo(tempDll).Length > 1_000_000)
            {
                File.Move(tempDll, targetDll, overwrite: true);
                log(Strings.T(lang, "inst_engine_at", targetDll));
                return;
            }
        }
        catch { }

        await DownloadEngineAsync(root, log, lang).WaitAsync(cancel);
    }

    /// <summary>Файлы прошлой версии: их можно и нужно затирать, данных в них нет.</summary>
    private static readonly string[] VersionLeftovers =
    {
        "*.old", "*.new", "singbox.json", "panel.port", "cehoproxy.pid",
        "sing-box-*.zip", "sing-box-*.tar.gz",
        // Прошлые версии вели отдельный лог движка и файл на каждое падение;
        // теперь всё это в одном журнале, а файлы только мусорят в папке.
        "sing-box.log", "sing-box.log.1", "crash-*.log",
        // Список «своих» адаптеров мог содержать чужой GUID, пока мы жили
        // на заводском адресе движка. При обновлении лучше записать заново.
        "tun-devices.txt",
    };

    /// <summary>Данные пользователя: настройки и сохранённые копии подписок.</summary>
    private static readonly string[] DataFiles = { "config.json", "sub-*.txt" };

    public sealed record Replaced(bool WasRunning, bool AutostartWasOn, int Wiped, int DataKept);

    /// <summary>
    /// Готовит папку под новую версию: снимает работающую защиту и убирает файлы прошлой
    /// сборки, но не трогает настройки и сохранённые подписки — ставить «поверх» и терять
    /// при этом подписки нельзя, а держать рядом две версии тем более.
    /// </summary>
    public static Replaced PrepareForNewVersion(string root, Action<string> log, string lang = "ru")
    {
        if (!Directory.Exists(root))
            return new Replaced(false, false, 0, 0);

        var autostartWasOn = Autostart.IsEnabled();
        var wasRunning = DaemonControl.IsRunning(root);

        if (wasRunning || autostartWasOn)
        {
            log(Strings.T(lang, "inst_stopping"));
            if (autostartWasOn) Autostart.StopService();
            if (DaemonControl.RequestStop(root)) Thread.Sleep(3000);
        }

        // Движок мог остаться после сбоя uninstall или удаления папки:
        // процесс жив и держит libcronet.dll, а pid-файл уже нет.
        TunCleanup.KillOurProcesses(Path.Combine(root, "singbox.json"), _ => { });
        if (DaemonControl.IsRunning(root))
        {
            TunCleanup.RemoveLeftovers(
                log, CehoConfig.Load(Path.Combine(root, "config.json")).TunAddress, root);
            DaemonControl.ClearRunning(root);
        }

        var (wiped, kept) = WipeVersionLeftovers(root);

        log(Strings.T(lang, "inst_wiped", wiped));
        log(Strings.T(lang, "inst_kept", kept));
        return new Replaced(wasRunning, autostartWasOn, wiped, kept);
    }

    /// <summary>
    /// Убирает файлы прошлой сборки и считает, сколько пользовательских файлов осталось
    /// нетронутыми. Возвращает: сколько затёрли и сколько данных сохранили.
    /// </summary>
    public static (int Wiped, int DataKept) WipeVersionLeftovers(string root)
    {
        var kept = 0;
        foreach (var pattern in DataFiles)
            try { kept += Directory.GetFiles(root, pattern).Length; } catch { }

        var wiped = 0;
        foreach (var pattern in VersionLeftovers)
        {
            try
            {
                foreach (var file in Directory.GetFiles(root, pattern))
                {
                    try { File.Delete(file); wiped++; } catch { }
                }
            }
            catch { }
        }

        try
        {
            var temp = Path.Combine(root, "engine-tmp");
            if (Directory.Exists(temp)) { Directory.Delete(temp, true); wiped++; }
        }
        catch { }

        return (wiped, kept);
    }

    public static string Install(string root, Action<string> log, string lang = "ru")
    {
        Directory.CreateDirectory(root);

        var target = BinaryPath(root);
        var self = Environment.ProcessPath
                   ?? throw new InvalidOperationException("не удалось определить путь к программе");

        if (!Os.RealPath(self).Equals(Os.RealPath(target), StringComparison.OrdinalIgnoreCase))
        {
            var backup = target + ".old";
            try { if (File.Exists(backup)) File.Delete(backup); } catch { }
            if (File.Exists(target)) File.Move(target, backup, overwrite: true);
            File.Copy(self, target, overwrite: true);
            log(Strings.T(lang, "inst_binary_at", target));
        }

        if (!Os.IsWindows) Os.Run("chmod", $"755 {target}", 5000);

        var ownDir = Path.GetDirectoryName(self);
        if (!string.IsNullOrEmpty(ownDir))
        {
            var nearbyEngine = Path.Combine(ownDir, Os.EngineFileName);
            if (!File.Exists(nearbyEngine)) nearbyEngine = Path.Combine(ownDir, Os.SingBoxFileName);
            var targetEngine = Path.Combine(root, Os.EngineFileName);
            if (File.Exists(nearbyEngine) && !Os.RealPath(nearbyEngine).Equals(Os.RealPath(targetEngine), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(nearbyEngine, targetEngine, overwrite: true);
                if (!Os.IsWindows) Os.Run("chmod", $"755 {targetEngine}", 5000);
                log(Strings.T(lang, "inst_engine_at", targetEngine));
            }

            CopyCronetDependencies(ownDir, root);
        }

        Os.AdoptOwnEngine(root);

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

    private static void AddToPath(string root, Action<string> log, string lang)
    {
        if (Os.IsWindows)
        {
            var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            if (current.Split(';').Any(p => p.Trim().TrimEnd('\\')
                    .Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                RefreshProcessPath(root);
                return;
            }
            try
            {
                Environment.SetEnvironmentVariable("Path",
                    current.TrimEnd(';') + ";" + root, EnvironmentVariableTarget.Machine);
                RefreshProcessPath(root);
                log(Strings.T(lang, "inst_alias_ok"));
                log(Strings.T(lang, "inst_alias_reopen"));
            }
            catch (Exception ex)
            {
                log(Strings.T(lang, "inst_path_failed", ex.Message));
            }
            return;
        }

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

    private static void RefreshProcessPath(string root)
    {
        try
        {
            var process = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process) ?? "";
            if (!process.Split(';').Any(p => p.Trim().TrimEnd('\\')
                    .Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                Environment.SetEnvironmentVariable("Path",
                    root + ";" + process, EnvironmentVariableTarget.Process);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? newName, int flags);

    private const int DelayUntilReboot = 0x4;

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

    public static async Task<string> DownloadEngineAsync(string root, Action<string> log, string lang = "ru")
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "amd64";
        var os = Os.Kind switch { OsKind.Windows => "windows", OsKind.Mac => "darwin", _ => "linux" };

        using var http = DirectHttp.CreateClient(TimeSpan.FromMinutes(10));
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

        var engine = Path.Combine(root, Os.EngineFileName);
        Extract(archive, root, engine);
        try { File.Delete(archive); } catch { }

        if (!File.Exists(engine))
            throw new InvalidOperationException("движок скачался, но распаковать его не удалось");

        if (MissingCronetDll(root))
            throw new InvalidOperationException(Strings.T(lang, "engine_cronet_still_missing", root));

        Os.AdoptOwnEngine(root);

        if (!Os.IsWindows) Os.Run("chmod", $"755 {engine}", 5000);
        log(Strings.T(lang, "inst_engine_at", engine));
        return engine;
    }

    private static void Extract(string archive, string root, string engine)
    {
        var temp = Path.Combine(root, "engine-tmp");
        try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        Directory.CreateDirectory(temp);

        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, temp, overwriteFiles: true);
        else
            Os.Run("tar", $"-xzf \"{archive}\" -C \"{temp}\"", 120000);

        var engineDir = Directory.EnumerateFiles(temp, Os.SingBoxFileName, SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(d => d is not null);
        var found = engineDir is null
            ? null
            : Path.Combine(engineDir, Os.SingBoxFileName);

        // libcronet.dll копируем первым: ceho-engine.exe может быть занят работающим демоном.
        if (engineDir is not null)
            CopyCronetDependencies(engineDir, root);

        if (found is not null && File.Exists(found))
        {
            try { File.Copy(found, engine, overwrite: true); }
            catch (IOException) when (File.Exists(engine)) { }
        }

        try { Directory.Delete(temp, true); } catch { }
    }
}

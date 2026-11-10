namespace ProxyCage.Core;

/// <summary>Помнит, вышла ли новая версия, чтобы панель показала кнопку «Обновить» без похода по настройкам.</summary>
public static class UpdateWatch
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(3);
    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(20);
    private static readonly object Gate = new();
    private static string? _version;
    private static DateTime _checkedUtc = DateTime.MinValue;
    private static DateTime _retryAfterUtc = DateTime.MinValue;
    private static bool _running;

    public static Func<string, Task<string?>> Probe { get; set; } = async repo => (await Updater.CheckAsync(repo))?.Version;

    public static string? Available
    {
        get
        {
            lock (Gate) return _version is { } v && Updater.IsNewerVersion(v, Updater.CurrentVersion) ? v : null;
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _version = null;
            _checkedUtc = DateTime.MinValue;
            _retryAfterUtc = DateTime.MinValue;
            _running = false;
        }
    }

    /// <summary>Не блокирует страницу: проверка идёт в фоне, результат виден при следующем показе.</summary>
    public static void RefreshInBackground(string repo, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        lock (Gate)
        {
            if (_running || string.IsNullOrWhiteSpace(repo)) return;
            if (at - _checkedUtc < Fresh || at < _retryAfterUtc) return;
            _running = true;
        }
        _ = Task.Run(async () =>
        {
            string? found = null;
            var ok = true;
            try { found = await Probe(repo); }
            catch { ok = false; }
            lock (Gate)
            {
                _running = false;
                if (ok) { _version = found; _checkedUtc = DateTime.UtcNow; }
                else _retryAfterUtc = DateTime.UtcNow + AfterFailure;
            }
        });
    }
}

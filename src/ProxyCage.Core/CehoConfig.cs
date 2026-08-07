using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProxyCage.Core;

/// <summary>Приложение, чей трафик обязан идти только через туннель.</summary>
public sealed class AppEntry
{
    /// <summary>Отображаемое имя (по умолчанию — имя папки).</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Папка, ВСЕ процессы под которой (включая дочерние) заворачиваются в туннель.
    /// Изоляция по папке, а не по .exe: Electron/Chromium поднимает несколько
    /// вспомогательных процессов, и правило по одному файлу их пропустит.
    /// </summary>
    public string Folder { get; set; } = "";

    /// <summary>Что запускать (может быть пусто — тогда приложение просто изолируется).</summary>
    public string? Launch { get; set; }

    /// <summary>
    /// Для MSIX/Store-приложений: в пути есть номер версии (Publisher.App_1.2.3.4_x64__hash),
    /// он меняется при обновлении. Тогда правило строится по префиксу до версии,
    /// иначе после апдейта приложение молча выпадет из-под изоляции.
    /// </summary>
    public bool VersionAgnostic { get; set; }

    /// <summary>
    /// Программа лежит в системном каталоге (/usr/bin и подобные), изолировать его целиком нельзя.
    /// Тогда в <see cref="Folder"/> лежит путь к самому файлу, и правило строится по нему одному.
    /// </summary>
    public bool SingleFile { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed class SubscriptionEntry
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Результат последней проверки живости (null — не проверялась).</summary>
    public bool? LastCheckOk { get; set; }
    public string? LastCheckedUtc { get; set; }
}

/// <summary>Единый конфиг CehoProxy — им пользуются и CLI, и веб-интерфейс.</summary>
public sealed class CehoConfig
{
    /// <summary>Порт локального веб-интерфейса. Спрашивается при установке.</summary>
    public int WebPort { get; set; } = 8899;

    /// <summary>Порт локального SOCKS/HTTP-входа (для ручной проверки и приложений без TUN).</summary>
    public int MixedPort { get; set; } = 2080;

    /// <summary>Подсеть TUN-интерфейса.</summary>
    public string TunAddress { get; set; } = "172.19.0.1/30";

    public List<AppEntry> Apps { get; set; } = new();
    public List<SubscriptionEntry> Subscriptions { get; set; } = new();

    public string? ActiveSubscription { get; set; }

    /// <summary>Пустой список = любая страна. Иначе пул ограничен этими странами.</summary>
    public List<string> PreferredCountries { get; set; } = new();

    /// <summary>Страны, чьи ноды не берём для выхода (RU по умолчанию — смысл продукта).</summary>
    public List<string> ExcludedCountries { get; set; } = new() { "RU" };

    /// <summary>Автоматически переключаться на живую ноду, если текущая перестала работать.</summary>
    public bool RotationEnabled { get; set; } = true;

    /// <summary>
    /// Не брать в пул ноды медленнее этого числа миллисекунд. null — брать любые.
    ///
    /// Отсев идёт по СОХРАНЁННОМУ замеру: мерить при каждой сборке правил значило бы
    /// ждать по несколько секунд на ровном месте. Нода без замера считается годной —
    /// «не измеряли» и «медленная» это разные вещи, и у hysteria2 с tuic замера не будет
    /// никогда: они поверх UDP, а TCP-проба соврала бы.
    /// </summary>
    public int? MaxLatencyMs { get; set; }

    /// <summary>Последние замеры задержки: ключ ноды (протокол|адрес|порт) → миллисекунды.</summary>
    public Dictionary<string, int> NodeLatency { get; set; } = new();

    /// <summary>
    /// Язык интерфейса и сообщений: ru или en. Спрашивается при первой настройке —
    /// у части машин нет GUI, и терминал должен говорить на понятном языке сразу.
    /// </summary>
    public string Language { get; set; } = "ru";

    /// <summary>
    /// Хеш пароля панели и CLI (PBKDF2). Пусто — доступ без пароля.
    /// На многопользовательской машине пароль обязателен: панель слушает петлю,
    /// а петля доступна КАЖДОМУ, кто вошёл на этот сервер.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Соль пароля, base64.</summary>
    public string? PasswordSalt { get; set; }

    /// <summary>Первичная настройка пройдена — мастер больше не навязывается.</summary>
    public bool SetupDone { get; set; }

    /// <summary>Репозиторий обновлений на GitHub в виде «владелец/имя».</summary>
    public string UpdateRepo { get; set; } = "CodoCeh/CehoProxy";

    /// <summary>
    /// Адрес, по которому проверяется живость подписки и принимается решение о ротации.
    /// Задаётся пользователем: «подписка жива» означает «доходит до того, что нужно ИМЕННО ТЕБЕ».
    /// Нода может отвечать и при этом не пускать на нужный сайт, поэтому проверка идёт
    /// настоящим запросом, а не пингом.
    /// </summary>
    public string CheckUrl { get; set; } = "https://www.gstatic.com/generate_204";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CehoConfig Load(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<CehoConfig>(File.ReadAllText(path), Json) ?? new CehoConfig()
            : new CehoConfig();

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}

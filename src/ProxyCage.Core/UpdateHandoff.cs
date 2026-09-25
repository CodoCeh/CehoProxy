using System.Text.Json;

namespace ProxyCage.Core;

public static class UpdateHandoff
{
    public const int PendingExitCode = 2;
    private const string FileName = "update-status.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed record Status(string JobId, string State, string Version, string Message);

    public static string PathFor(string root) => Path.Combine(root, FileName);

    public static Status Pending(string jobId, string version) =>
        new(jobId, "pending", version, "Проверяю установку после перезапуска.");

    public static Status Verified(string jobId, string version) =>
        new(jobId, "verified", version, $"Установка версии {version} проверена.");

    public static Status Failed(string jobId, string version) =>
        new(jobId, "failed", version, "Не удалось проверить обновление. Подробности в журнале обновления.");

    public static void Write(string root, Status status)
    {
        Directory.CreateDirectory(root);
        var path = PathFor(root);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    public static Status? Read(string root)
    {
        try
        {
            var path = PathFor(root);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Status>(File.ReadAllText(path), JsonOptions);
        }
        catch { return null; }
    }

    public static bool IsTerminal(Status? status) => status?.State is "verified" or "failed";

    public static bool MatchesJob(Status? status, string? jobId, bool handoffStageReached) =>
        status is not null && status.JobId == jobId && handoffStageReached;

    public static int ExitCode(Status? status) => status?.State switch
    {
        "verified" => 0,
        "pending" => PendingExitCode,
        _ => 1,
    };
}

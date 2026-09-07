using System.Globalization;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>
/// Сколько подписке осталось жить и сколько трафика в ней израсходовано.
/// Провайдеры отдают это заголовком subscription-userinfo; кто не отдаёт — иногда
/// пишет дату в подписи ноды.
/// </summary>
public sealed record SubscriptionInfo(
    DateTime? ExpiresUtc, long? UploadBytes, long? DownloadBytes, long? TotalBytes)
{
    public const string HeaderName = "subscription-userinfo";

    public long? UsedBytes =>
        UploadBytes is null && DownloadBytes is null
            ? null
            : (UploadBytes ?? 0) + (DownloadBytes ?? 0);

    public bool IsEmpty => ExpiresUtc is null && UsedBytes is null && TotalBytes is null;

    public static SubscriptionInfo? FromHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;

        long? upload = null, download = null, total = null;
        DateTime? expires = null;

        foreach (var part in header.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;

            var key = part[..eq].Trim().ToLowerInvariant();
            var raw = part[(eq + 1)..].Trim();
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) continue;

            switch (key)
            {
                case "upload": upload = value; break;
                case "download": download = value; break;
                // total=0 у провайдеров означает «без лимита», а не «ноль байт»
                case "total": total = value > 0 ? value : null; break;
                case "expire": expires = FromUnixSeconds(value); break;
            }
        }

        var info = new SubscriptionInfo(expires, upload, download, total);
        return info.IsEmpty ? null : info;
    }

    private static DateTime? FromUnixSeconds(long seconds)
    {
        if (seconds <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime; }
        catch { return null; }
    }

    private static readonly string[] ExpiryWords =
    {
        "expire", "expires", "expiry", "expired", "valid until", "until",
        "истекает", "истекло", "действует до", "срок",
        "到期", "过期", "剩余时间", "有效期",
    };

    private static readonly Regex[] DatePatterns =
    {
        new(@"(?<y>20\d{2})[-/.](?<m>\d{1,2})[-/.](?<d>\d{1,2})", RegexOptions.Compiled),
        new(@"(?<d>\d{1,2})[-/.](?<m>\d{1,2})[-/.](?<y>20\d{2})", RegexOptions.Compiled),
    };

    /// <summary>
    /// Дата из подписи ноды. Берётся только рядом со словом про срок: иначе в дату
    /// превращается любое число из имени вроде «NL 2026 10.05».
    /// </summary>
    public static DateTime? ExpiryFromRemarks(IEnumerable<string> remarks)
    {
        foreach (var remark in remarks)
        {
            if (string.IsNullOrWhiteSpace(remark)) continue;

            var lower = remark.ToLowerInvariant();
            if (!ExpiryWords.Any(w => lower.Contains(w, StringComparison.Ordinal))) continue;

            foreach (var pattern in DatePatterns)
            {
                var m = pattern.Match(remark);
                if (!m.Success) continue;

                var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
                var mo = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
                var d = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
                if (mo is < 1 or > 12 || d is < 1 or > 31) continue;

                try { return new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc); }
                catch { }
            }
        }
        return null;
    }

    public static string Bytes(long value)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0
            ? $"{value} {units[0]}"
            : $"{size.ToString(size >= 100 ? "F0" : "F1", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    /// <summary>Целых суток до конца подписки; отрицательное — уже истекла.</summary>
    public static int DaysLeft(DateTime expiresUtc) =>
        (int)Math.Floor((expiresUtc - DateTime.UtcNow).TotalDays);
}

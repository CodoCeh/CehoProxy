namespace ProxyCage.Core;

/// <summary>Сравнение Windows-пользователей (DOMAIN\sam, UPN, SAM) — для RDS, чтобы не трогать чужие сеансы.</summary>
public static class AppIsolation
{
    public static string CurrentUser() => Environment.UserName;

    public static bool SameWindowsUser(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return Sam(a).Equals(Sam(b), StringComparison.OrdinalIgnoreCase);
    }

    public static string Sam(string user)
    {
        var slash = user.LastIndexOf('\\');
        if (slash >= 0) user = user[(slash + 1)..];
        var at = user.IndexOf('@');
        if (at >= 0) user = user[..at];
        return user.Trim();
    }

    public static bool IsServiceAccount(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return true;
        var sam = Sam(user);
        return sam.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
               || sam.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase)
               || sam.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase)
               || sam.Equals("ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase);
    }
}

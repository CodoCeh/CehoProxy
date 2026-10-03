using System.Text;

namespace ProxyCage.Core;

/// <summary>A locally generated, deliberately allowlisted report. Never sends data anywhere.</summary>
public static class SupportReport
{
    public const string FileName = "cehoproxy-support.txt";
    [Flags]
    public enum Parts { None = 0, Environment = 1, Configuration = 2, Diagnostics = 4, Logs = 8, All = 15 }
    public const Parts DefaultParts = Parts.Environment | Parts.Configuration | Parts.Diagnostics;

    public static Parts PartsFromForm(IReadOnlyDictionary<string, string> form)
    {
        var result = Parts.None;
        if (form.ContainsKey("report_environment")) result |= Parts.Environment;
        if (form.ContainsKey("report_configuration")) result |= Parts.Configuration;
        if (form.ContainsKey("report_diagnostics")) result |= Parts.Diagnostics;
        if (form.ContainsKey("report_logs")) result |= Parts.Logs;
        return result;
    }

    public static string Create(CehoConfig cfg, Parts parts = DefaultParts,
        WebServer.ControlState? state = null, Doctor.Result? diagnostics = null,
        IEnumerable<string>? logs = null)
    {
        var sb = new StringBuilder("CehoProxy support report\n");
        sb.AppendLine($"Generated UTC: {DateTime.UtcNow:O}");
        sb.AppendLine("Privacy: local only. No credentials, subscription links, host addresses or application paths included.");
        sb.AppendLine("Free-form log and diagnostic messages are replaced with known issue categories. Review before sharing.");
        if (parts == Parts.None) sb.AppendLine("No sections selected.");
        if (parts.HasFlag(Parts.Environment))
        {
            sb.AppendLine("\n[Environment]");
            sb.AppendLine($"CehoProxy: {Updater.CurrentVersion}");
            sb.AppendLine($"Platform: {Os.Kind}; architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
            sb.AppendLine($"Runtime: {Environment.Version}");
        }
        if (parts.HasFlag(Parts.Configuration))
        {
            sb.AppendLine("\n[Configuration summary]");
            sb.AppendLine($"Panel mode: {(cfg.SimplePanel ? "simple" : "pro")}");
            sb.AppendLine($"Apps: {cfg.Apps.Count}; enabled: {cfg.Apps.Count(a => a.Enabled)}; blocked: {cfg.Apps.Count(a => a.NoInternet)}");
            sb.AppendLine($"Subscriptions: {cfg.Subscriptions.Count}; enabled: {cfg.Subscriptions.Count(s => s.Enabled)}");
            sb.AppendLine($"Subscription checks: ok {cfg.Subscriptions.Count(s => s.LastCheckOk == true)}, failed {cfg.Subscriptions.Count(s => s.LastCheckOk == false)}, unknown {cfg.Subscriptions.Count(s => s.LastCheckOk is null)}");
            sb.AppendLine($"Sites: {cfg.DirectSites.Count}; mode: {(cfg.SitesOnly ? "only" : "except")}");
            sb.AppendLine($"Fail closed: {cfg.FailClosed}; IPv6: {cfg.TunIpv6}; rotation: {cfg.RotationEnabled}");
            sb.AppendLine($"Country filters: preferred {cfg.PreferredCountries.Count}, excluded {cfg.ExcludedCountries.Count}; disabled nodes: {cfg.BlockedNodes.Count}");
            sb.AppendLine($"TLS verification disabled in direct subscription entries: {cfg.Subscriptions.Count(HasInsecureTls)}");
            sb.AppendLine("Names, links, node credentials, addresses, ports, keys, paths and panel password data: [redacted]");
        }
        if (parts.HasFlag(Parts.Diagnostics))
        {
            sb.AppendLine("\n[Diagnostics]");
            sb.AppendLine(state is null ? "Engine state: unavailable" : $"Engine running: {state.Running}; exit checked: {state.Probed}");
            if (state?.LastError is { Length: > 0 } error) sb.AppendLine("Last issue: " + IssueCategory(error));
            if (diagnostics is null) sb.AppendLine("No saved diagnostic check. Creating this report does not run network tests.");
            else
            {
                sb.AppendLine($"Saved checks: {diagnostics.Checks.Count}; blockers: {diagnostics.Blockers}; warnings: {diagnostics.Warnings}");
                foreach (var check in diagnostics.Checks)
                    sb.AppendLine($"- {check.Level}; repair: {check.Repair}; issue: {IssueCategory(check.ToString())}");
            }
            foreach (var sub in cfg.Subscriptions.Where(s => !string.IsNullOrWhiteSpace(s.LastError)))
                sb.AppendLine("Subscription issue: " + IssueCategory(sub.LastError!));
        }
        if (parts.HasFlag(Parts.Logs))
        {
            sb.AppendLine("\n[Recent event summary]");
            var recent = (logs ?? Array.Empty<string>()).TakeLast(100).ToList();
            sb.AppendLine($"Events: {recent.Count} (at most 100); raw messages omitted for privacy.");
            foreach (var group in recent.GroupBy(IssueCategory).OrderByDescending(g => g.Count()))
                sb.AppendLine($"- {group.Key}: {group.Count()}");
        }
        return sb.ToString();
    }

    // The report never copies free-form messages. A novel log shape cannot bypass redaction.
    internal static string IssueCategory(string text)
    {
        var value = text.ToLowerInvariant();
        if (Contains(value, "certificate", "сертификат", "x509", "tls handshake")) return "TLS / certificate verification";
        if (Contains(value, "authentication", "unauthorized", "forbidden", "407", "авторизац")) return "Authentication / access refused";
        if (Contains(value, "timeout", "timed out", "deadline", "таймаут", "время ожидания")) return "Timeout";
        if (Contains(value, "connection refused", "соединение отклонено")) return "Connection refused";
        if (Contains(value, "dns", "resolve", "no such host", "имя хоста")) return "DNS / name resolution";
        if (Contains(value, "address already in use", "port is busy", "порт занят")) return "Local port conflict";
        if (Contains(value, "wintun", "tun interface", "адаптер")) return "Tunnel adapter";
        if (Contains(value, "permission denied", "access denied", "нет прав")) return "Local permissions";
        if (Contains(value, "empty pool", "no nodes", "нет нод", "пул пуст")) return "No usable nodes";
        if (Contains(value, "fatal", "panic", "crash", "падение")) return "Process failure";
        if (Contains(value, "error", "failed", "ошибк", "не удалось")) return "Other error (details redacted)";
        return "Other event (details redacted)";
    }

    private static bool Contains(string text, params string[] words) => words.Any(w => text.Contains(w, StringComparison.Ordinal));

    public static bool HasInsecureTls(SubscriptionEntry subscription) => HasInsecureTls(subscription, null);

    public static bool HasInsecureTls(SubscriptionEntry subscription, string? root)
    {
        try
        {
            if (NaiveProxyHelper.TryParseUri(subscription.Url, out var settings)) return settings?.AllowInsecure == true;
            if (SubscriptionParser.LooksLikeNodeUri(subscription.Url))
                return SubscriptionParser.Parse(subscription.Url).Any(n => n.AllowInsecure);
            // Read only the already-downloaded local cache; rendering must never fetch a subscription.
            var name = "sub-" + subscription.Name + ".txt";
            if (root is not null && name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0)
            {
                var path = Path.Combine(root, name);
                var file = new FileInfo(path);
                if (file.Exists && file.Length <= 4 * 1024 * 1024)
                    return SubscriptionParser.Parse(File.ReadAllText(path)).Any(n => n.AllowInsecure);
            }
        }
        catch { }
        return false;
    }
}

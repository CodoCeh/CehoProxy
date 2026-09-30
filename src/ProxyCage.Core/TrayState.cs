using System.Text.Json;

namespace ProxyCage.Core;

public enum TrayLook { Protected, Starting, Off, Trouble, Stopped, Locked }

public enum TrayBadge { Disc, Half, Ring, Slash, Square, Lock }

public static class TrayState
{
    public const int PollSeconds = 5;

    public const int MaxWaitSeconds = 300;

    public const string TooltipLimitMarker = "…";

    public static readonly string[] StatusArgs = { "status", "--json", "--quick" };

    public static readonly string[] FullStatusArgs = { "status", "--json" };

    public sealed record Snapshot(
        bool Running, bool Starting, bool Daemon, int Apps,
        string? ExitCountry, string? ExitIp, bool ExitProbed, bool TrayControls = false, bool Recovering = false);

    public static string? Unwrap(string? apiBody)
    {
        if (string.IsNullOrWhiteSpace(apiBody)) return null;
        try
        {
            using var doc = JsonDocument.Parse(apiBody);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("text", out var text)
                ? text.GetString()
                : null;
        }
        catch { return null; }
    }

    public static Snapshot? Parse(string? json, bool exitProbed = false)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json.Trim());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("daemon", out _)) return null;
            return new Snapshot(
                Flag(root, "running"), Flag(root, "starting"), Flag(root, "daemon"),
                Count(root, "apps"), Text(root, "exitCountry"), Text(root, "exitIp"), exitProbed,
                Flag(root, "trayControls"), Flag(root, "recovering"));
        }
        catch { return null; }
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int Count(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static Snapshot Remember(Snapshot fresh, Snapshot? previous) =>
        fresh is { Running: true, ExitProbed: false } && previous is { Running: true, ExitProbed: true }
            ? fresh with
            {
                ExitCountry = previous.ExitCountry, ExitIp = previous.ExitIp, ExitProbed = true,
            }
            : fresh;

    public static bool NeedsExitProbe(Snapshot? snapshot) =>
        snapshot is { Running: true, ExitProbed: false };

    public static TrayLook Look(Snapshot? snapshot, bool locked) =>
        locked ? TrayLook.Locked
        : snapshot is null || !snapshot.Daemon ? TrayLook.Stopped
        : snapshot is { Running: true, ExitProbed: true, ExitIp: null } ? TrayLook.Trouble
        : snapshot.Running ? TrayLook.Protected
        : snapshot.Starting || snapshot.Recovering ? TrayLook.Starting
        : TrayLook.Off;

    public static TrayBadge Badge(TrayLook look) => look switch
    {
        TrayLook.Protected => TrayBadge.Disc,
        TrayLook.Starting => TrayBadge.Half,
        TrayLook.Off => TrayBadge.Ring,
        TrayLook.Trouble => TrayBadge.Slash,
        TrayLook.Stopped => TrayBadge.Square,
        _ => TrayBadge.Lock,
    };

    public static bool ShowsControls(Snapshot? snapshot) => snapshot is { TrayControls: true };

    public static bool CanTurnOn(TrayLook look) => look == TrayLook.Off;

    public static bool CanTurnOff(TrayLook look) =>
        look is TrayLook.Protected or TrayLook.Starting or TrayLook.Trouble;

    public static bool HidesDetails(TrayLook look) => look == TrayLook.Locked;

    public static string StateText(string? lang, TrayLook look, int waitSeconds = 0, bool recovering = false) => look switch
    {
        TrayLook.Protected => Strings.T(lang, "state_on"),
        TrayLook.Starting => Strings.T(lang, recovering ? "state_recovering" : "state_starting"),
        TrayLook.Off => Strings.T(lang, "state_off"),
        TrayLook.Trouble => Strings.T(lang, "hero_no_exit"),
        TrayLook.Locked => waitSeconds > 0
            ? Strings.T(lang, "tray_wait", waitSeconds)
            : Strings.T(lang, "tray_need_password"),
        _ => Strings.T(lang, "tray_no_service"),
    };

    public static string? Hint(string? lang, TrayLook look, int waitSeconds = 0) => look switch
    {
        TrayLook.Stopped => Strings.T(lang, "tray_no_service_hint", Os.IsWindows ? "" : "sudo "),
        TrayLook.Trouble => Strings.T(lang, "state_no_exit"),
        TrayLook.Locked => Strings.T(lang, waitSeconds > 0 ? "tray_wait_hint" : "tray_password_hint"),
        _ => null,
    };

    public static string Tooltip(
        string? lang, TrayLook look, Snapshot? snapshot, int waitSeconds = 0, int limit = 127)
    {
        var text = "CehoProxy — " + StateText(lang, look, waitSeconds, snapshot?.Recovering == true);
        if (!HidesDetails(look) && look == TrayLook.Protected && snapshot is { ExitIp: { Length: > 0 } ip })
            text += " · " + Strings.T(lang, "exit_is", snapshot.ExitCountry ?? "?", ip);
        return limit > 1 && text.Length > limit
            ? text[..(limit - 1)].TrimEnd() + TooltipLimitMarker
            : text;
    }

    public static int RetryAfterSeconds(string? header)
    {
        if (!int.TryParse(header?.Trim(), out var seconds)) return PollSeconds;
        return Math.Clamp(seconds, 1, MaxWaitSeconds);
    }
}

public sealed class TrayNotifier
{
    private bool _started;
    private bool _lost;
    private TrayLook _last;
    private string? _country;

    public string? Next(string? lang, TrayLook look, TrayState.Snapshot? snapshot)
    {
        var country = look == TrayLook.Protected ? snapshot?.ExitCountry : null;
        string? notice = null;
        if (!_started || look == TrayLook.Locked || _last == TrayLook.Locked)
        {
            _started = true;
        }
        else if (look == TrayLook.Protected)
        {
            if (_lost)
                notice = Strings.T(lang, "state_on") + (snapshot is { ExitIp: { Length: > 0 } ip }
                    ? " · " + Strings.T(lang, "exit_is", snapshot.ExitCountry ?? "?", ip) : "");
            else if (_last == TrayLook.Protected && _country is not null && country is not null && _country != country)
                notice = Strings.T(lang, "notice_exit_changed", _country, country);
            _lost = false;
        }
        else if (!_lost && (_last == TrayLook.Protected || _last == TrayLook.Starting))
        {
            var reason = look switch
            {
                TrayLook.Off when _last == TrayLook.Protected => Strings.T(lang, "notice_off"),
                TrayLook.Trouble => Strings.T(lang, "notice_trouble"),
                TrayLook.Stopped => Strings.T(lang, "notice_service_gone"),
                TrayLook.Starting when snapshot?.Recovering == true => Strings.T(lang, "state_recovering"),
                _ => null,
            };
            if (reason is not null) { notice = reason; _lost = true; }
        }

        _last = look;
        if (country is not null) _country = country;
        return notice;
    }
}

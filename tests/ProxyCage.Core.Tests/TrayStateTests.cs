using ProxyCage.Core;
using Xunit;

namespace ProxyCage.Core.Tests;

public class TrayStateTests
{
    private const string Live = """
        {"running":true,"starting":false,"daemon":true,"leakGuard":false,"apps":2,"subscriptions":1,"exitCountry":"NL","exitIp":"203.0.113.9","password":false,"version":"1.2.73"}
        """;

    private const string Down = """
        {"running":false,"starting":false,"daemon":false,"leakGuard":false,"apps":0,"subscriptions":0,"exitCountry":null,"exitIp":null,"password":false,"version":"1.2.73"}
        """;

    private static TrayState.Snapshot Probed(string json) => TrayState.Parse(json, true)!;

    [Fact]
    public void ReadsTheJsonTheCliPrints()
    {
        var snapshot = TrayState.Parse(Live, true);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Running);
        Assert.True(snapshot.Daemon);
        Assert.Equal(2, snapshot.Apps);
        Assert.Equal("NL", snapshot.ExitCountry);
        Assert.Equal("203.0.113.9", snapshot.ExitIp);
        Assert.True(snapshot.ExitProbed);
    }

    [Fact]
    public void TakesTheAnswerOutOfTheApiEnvelope()
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new { ok = true, text = Live });
        Assert.Equal(Live, TrayState.Unwrap(body));
        Assert.NotNull(TrayState.Parse(TrayState.Unwrap(body)));
    }

    [Fact]
    public void FailedApiCallLeavesNoSnapshot()
    {
        Assert.Null(TrayState.Unwrap(""));
        Assert.Null(TrayState.Unwrap("not json"));
        Assert.Null(TrayState.Parse(null));
        Assert.Null(TrayState.Parse("{}"));
    }

    [Fact]
    public void ColourFollowsTheState()
    {
        Assert.Equal(TrayLook.Protected, TrayState.Look(Probed(Live), false));
        Assert.Equal(TrayLook.Stopped, TrayState.Look(Probed(Down), false));
        Assert.Equal(TrayLook.Stopped, TrayState.Look(null, false));
        Assert.Equal(TrayLook.Locked, TrayState.Look(Probed(Live), true));
    }

    [Fact]
    public void TunnelWithoutAnyAnsweringNodeIsTrouble()
    {
        var blind = Probed(Live
            .Replace("\"exitCountry\":\"NL\"", "\"exitCountry\":null")
            .Replace("\"exitIp\":\"203.0.113.9\"", "\"exitIp\":null"));
        Assert.Equal(TrayLook.Trouble, TrayState.Look(blind, false));
        Assert.True(TrayState.CanTurnOff(TrayLook.Trouble));
        Assert.False(TrayState.CanTurnOn(TrayLook.Trouble));
        Assert.Equal(Strings.T("ru", "hero_no_exit"), TrayState.StateText("ru", TrayLook.Trouble));
    }

    [Fact]
    public void UnprobedRunningStateIsNotMistakenForTrouble()
    {
        var quick = TrayState.Parse(Live
            .Replace("\"exitCountry\":\"NL\"", "\"exitCountry\":null")
            .Replace("\"exitIp\":\"203.0.113.9\"", "\"exitIp\":null"));
        Assert.Equal(TrayLook.Protected, TrayState.Look(quick, false));
        Assert.True(TrayState.NeedsExitProbe(quick));
    }

    [Fact]
    public void DaemonWithoutTunnelIsOffAndCanBeTurnedOn()
    {
        var idle = Probed(Live.Replace("\"running\":true", "\"running\":false"));
        Assert.Equal(TrayLook.Off, TrayState.Look(idle, false));
        Assert.True(TrayState.CanTurnOn(TrayLook.Off));
        Assert.False(TrayState.CanTurnOff(TrayLook.Off));
    }

    [Fact]
    public void StartingIsShownWhileTheTunnelComesUp()
    {
        var rising = Probed(Live
            .Replace("\"running\":true", "\"running\":false")
            .Replace("\"starting\":false", "\"starting\":true"));
        Assert.Equal(TrayLook.Starting, TrayState.Look(rising, false));
        Assert.True(TrayState.CanTurnOff(TrayLook.Starting));
        Assert.False(TrayState.CanTurnOn(TrayLook.Starting));
    }

    [Fact]
    public void NothingIsClickableWithoutTheServiceOrThePassword()
    {
        foreach (var look in new[] { TrayLook.Stopped, TrayLook.Locked })
        {
            Assert.False(TrayState.CanTurnOn(look));
            Assert.False(TrayState.CanTurnOff(look));
            Assert.False(string.IsNullOrWhiteSpace(TrayState.Hint("ru", look)));
        }
        Assert.Null(TrayState.Hint("ru", TrayLook.Protected));
    }

    [Fact]
    public void EveryStateHasItsOwnShapeNotJustAColour()
    {
        var looks = Enum.GetValues<TrayLook>();
        var badges = looks.Select(TrayState.Badge).ToList();
        Assert.Equal(looks.Length, badges.Distinct().Count());
        Assert.Equal(TrayBadge.Disc, TrayState.Badge(TrayLook.Protected));
        Assert.Equal(TrayBadge.Ring, TrayState.Badge(TrayLook.Off));
        Assert.Equal(TrayBadge.Slash, TrayState.Badge(TrayLook.Trouble));
        Assert.Equal(TrayBadge.Lock, TrayState.Badge(TrayLook.Locked));
        Assert.NotEqual(TrayState.Badge(TrayLook.Protected), TrayState.Badge(TrayLook.Off));
        Assert.NotEqual(TrayState.Badge(TrayLook.Off), TrayState.Badge(TrayLook.Trouble));
    }

    [Fact]
    public void LockedStateTellsNothingAboutTheTunnel()
    {
        Assert.True(TrayState.HidesDetails(TrayLook.Locked));
        var tip = TrayState.Tooltip("ru", TrayLook.Locked, Probed(Live));
        Assert.DoesNotContain("203.0.113.9", tip);
        Assert.Contains(Strings.T("ru", "tray_need_password"), tip);
    }

    [Fact]
    public void WaitingAfterWrongPasswordsNamesTheSeconds()
    {
        Assert.Contains("40", TrayState.StateText("ru", TrayLook.Locked, 40));
        Assert.Contains("40", TrayState.Tooltip("ru", TrayLook.Locked, null, 40));
        Assert.Equal(Strings.T("ru", "tray_wait_hint"), TrayState.Hint("ru", TrayLook.Locked, 40));
        Assert.Equal(Strings.T("ru", "tray_password_hint"), TrayState.Hint("ru", TrayLook.Locked));
    }

    [Fact]
    public void RetryAfterIsTakenLiterallyAndKeptSane()
    {
        Assert.Equal(40, TrayState.RetryAfterSeconds("40"));
        Assert.Equal(5, TrayState.RetryAfterSeconds(" 5 "));
        Assert.Equal(TrayState.PollSeconds, TrayState.RetryAfterSeconds(null));
        Assert.Equal(TrayState.PollSeconds, TrayState.RetryAfterSeconds("завтра"));
        Assert.Equal(1, TrayState.RetryAfterSeconds("0"));
        Assert.Equal(TrayState.MaxWaitSeconds, TrayState.RetryAfterSeconds("99999"));
    }

    [Fact]
    public void ExitIsAskedForOncePerSessionNotOnEveryPoll()
    {
        var quick = TrayState.Parse(Live
            .Replace("\"exitCountry\":\"NL\"", "\"exitCountry\":null")
            .Replace("\"exitIp\":\"203.0.113.9\"", "\"exitIp\":null"))!;
        Assert.True(TrayState.NeedsExitProbe(quick));

        var merged = TrayState.Remember(quick, Probed(Live));
        Assert.False(TrayState.NeedsExitProbe(merged));
        Assert.Equal("203.0.113.9", merged.ExitIp);

        var after = TrayState.Remember(quick, Probed(Down));
        Assert.True(TrayState.NeedsExitProbe(after));
        Assert.False(TrayState.NeedsExitProbe(null));
    }

    [Fact]
    public void TooltipNamesTheProductStateAndExit()
    {
        var tip = TrayState.Tooltip("ru", TrayLook.Protected, Probed(Live));
        Assert.StartsWith("CehoProxy — ", tip);
        Assert.Contains(Strings.T("ru", "state_on"), tip);
        Assert.Contains("203.0.113.9", tip);

        var english = TrayState.Tooltip("en", TrayLook.Off, Probed(Down));
        Assert.Contains(Strings.T("en", "state_off"), english);
    }

    [Fact]
    public void TooltipStaysInsideTheSystemLimit()
    {
        var clipped = TrayState.Tooltip("ru", TrayLook.Starting, null, 0, 24);
        Assert.True(clipped.Length <= 24);
        Assert.EndsWith(TrayState.TooltipLimitMarker, clipped);
    }

    [Fact]
    public void EveryTrayPhraseHasBothLanguages()
    {
        foreach (var key in new[]
                 {
                     "tray_panel", "tray_quit", "tray_no_service", "tray_no_service_hint",
                     "tray_need_password", "tray_sign_in", "tray_password_hint",
                     "tray_password_prompt", "tray_wait", "tray_wait_hint", "tray_failed",
                     "btn_on", "btn_off", "hero_no_exit",
                 })
        {
            Assert.NotEqual(key, Strings.T("ru", key));
            Assert.NotEqual(key, Strings.T("en", key));
            Assert.NotEqual(Strings.T("ru", key), Strings.T("en", key));
        }
    }

    [Fact]
    public void StatusCommandIsTheCliOne()
    {
        Assert.Equal(new[] { "status", "--json", "--quick" }, TrayState.StatusArgs);
        Assert.Equal(new[] { "status", "--json" }, TrayState.FullStatusArgs);
        Assert.True(TrayState.PollSeconds >= 3);
    }

    [Fact]
    public void Recovery_is_shown_as_its_own_state()
    {
        var snap = TrayState.Parse("{\"daemon\":true,\"running\":false,\"starting\":true,\"recovering\":true}");
        var look = TrayState.Look(snap, false);

        Assert.Equal(TrayLook.Starting, look);
        Assert.True(TrayState.CanTurnOff(look));
        Assert.Contains(Strings.T("ru", "state_recovering"), TrayState.Tooltip("ru", look, snap, limit: 0));
        Assert.Equal(Strings.T("ru", "state_starting"), TrayState.StateText("ru", look));
    }
}

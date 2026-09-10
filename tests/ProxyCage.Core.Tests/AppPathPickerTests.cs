using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class AppPathPickerTests
{
    [Theory]
    [InlineData("bsv", "bsv", true)]
    [InlineData(@"SERVER\bsv", "bsv", true)]
    [InlineData("bsv@firma.local", "bsv", true)]
    [InlineData("bsv", "other", false)]
    public void Windows_user_names_compare_by_sam(string a, string b, bool same) =>
        Assert.Equal(same, AppIsolation.SameWindowsUser(a, b));

    [Fact]
    public void Rdp_without_console_user_uses_explorer_owner()
    {
        var chosen = AppPathPicker.ChooseInteractiveUser(
            computerSystemUser: "",
            environmentUser: "SYSTEM",
            explorerOwners: new[] { @"SERVER\bsv" },
            panelClientOwners: Array.Empty<string>());

        Assert.Equal(@"SERVER\bsv", chosen);
    }

    [Fact]
    public void Panel_client_wins_over_console_and_explorer()
    {
        var chosen = AppPathPicker.ChooseInteractiveUser(
            computerSystemUser: @"SERVER\console",
            environmentUser: "SYSTEM",
            explorerOwners: new[] { @"SERVER\console", @"SERVER\bsv" },
            panelClientOwners: new[] { "bsv" });

        Assert.Equal("bsv", chosen);
    }

    [Fact]
    public void Scheduled_task_user_is_enough_when_computer_system_is_empty()
    {
        var chosen = AppPathPicker.ChooseInteractiveUser(
            computerSystemUser: null,
            environmentUser: "bsv",
            explorerOwners: Array.Empty<string>(),
            panelClientOwners: Array.Empty<string>());

        Assert.Equal("bsv", chosen);
    }

    [Fact]
    public void System_account_is_not_treated_as_a_desktop_user()
    {
        var chosen = AppPathPicker.ChooseInteractiveUser(
            computerSystemUser: "SYSTEM",
            environmentUser: "SYSTEM",
            explorerOwners: Array.Empty<string>(),
            panelClientOwners: Array.Empty<string>());

        Assert.Null(chosen);
    }

    [Fact]
    public void Chromium_network_service_command_line_is_detected()
    {
        Assert.True(IsolatedAppBounce.IsNetworkServiceCommandLine(
            @"C:\Program Files\Google\Chrome\Application\chrome.exe --type=utility --utility-sub-type=network.mojom.NetworkService --lang=ru"));
        Assert.False(IsolatedAppBounce.IsNetworkServiceCommandLine(
            @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.False(IsolatedAppBounce.IsNetworkServiceCommandLine(
            @"C:\Program Files\Google\Chrome\Application\chrome.exe --type=renderer"));
        Assert.False(IsolatedAppBounce.IsNetworkServiceCommandLine(null));
    }
}

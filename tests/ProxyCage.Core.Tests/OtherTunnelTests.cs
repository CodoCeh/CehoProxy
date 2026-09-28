namespace ProxyCage.Core.Tests;

public sealed class OtherTunnelTests
{
    [Theory]
    [InlineData("Teredo Tunneling Pseudo-Interface", true)]
    [InlineData("Microsoft ISATAP Adapter #2", true)]
    [InlineData("Microsoft 6to4 Adapter", true)]
    [InlineData("Microsoft IP-HTTPS Platform Adapter", true)]
    [InlineData("WireGuard Tunnel", false)]
    [InlineData("AmneziaWG Tunnel", false)]
    [InlineData("TAP-Windows Adapter V9", false)]
    public void Built_in_windows_transition_adapters_are_not_someone_elses_vpn(string description, bool builtIn) =>
        Assert.Equal(builtIn, SystemProxy.IsWindowsTransitionAdapter(description));
}

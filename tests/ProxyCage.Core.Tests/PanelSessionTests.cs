namespace ProxyCage.Core.Tests;

[Collection("panel password")]
public class PanelSessionTests
{
    [Fact]
    public void Session_stops_working_when_the_password_changes_elsewhere()
    {
        var cfg = new CehoConfig();
        Auth.SetPassword(cfg, "first-pass");
        var token = Auth.IssueSession(cfg);

        Assert.True(Auth.ValidSession(cfg, token));

        var changed = new CehoConfig();
        Auth.SetPassword(changed, "second-pass");

        Assert.False(Auth.ValidSession(changed, token));
    }

    [Fact]
    public void Session_from_before_the_password_was_set_does_not_open_the_panel()
    {
        var open = new CehoConfig();
        var token = Auth.IssueSession(open);

        var closed = new CehoConfig();
        Auth.SetPassword(closed, "new-pass");

        Assert.False(Auth.ValidSession(closed, token));
    }
}

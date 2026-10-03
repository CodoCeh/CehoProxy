namespace ProxyCage.Core.Tests;

public class OwnedEngineProcessTests
{
    [Theory]
    [InlineData("ceho-engine.exe")] [InlineData("sing-box.exe")]
    public void Exact_config_argument_proves_owned_windows_engine(string name)
    {
        var config = @"C:\Program Data\CehoProxy\singbox.json";
        var line = $"\"C:\\Program Data\\CehoProxy\\{name}\" run --disable-color -c \"{config}\"";
        Assert.True(OwnedEngineProcess.Matches(name, OwnedEngineProcess.Arguments(line), config, windows: true));
    }

    [Theory]
    [InlineData("ceho-engine.exe run -c C:\\Other\\singbox.json")]
    [InlineData("ceho-engine.exe run -c C:\\CehoProxy\\singbox.json.backup")]
    [InlineData("ceho-engine.exe run --note C:\\CehoProxy\\singbox.json -c C:\\Other\\singbox.json")]
    [InlineData("ceho-engine.exe run -c C:\\CehoProxy\\singbox.json -c C:\\Other\\singbox.json")]
    [InlineData("ceho-engine.exe run -c C:\\CehoProxy\\singbox.json -C C:\\Other")]
    [InlineData("ceho-engine.exe run -c \"C:\\CehoProxy\\singbox.json")]
    [InlineData("")]
    public void Similar_name_home_substring_or_ambiguous_arguments_are_not_ownership(string line) =>
        Assert.False(OwnedEngineProcess.Matches("ceho-engine.exe", OwnedEngineProcess.Arguments(line),
            @"C:\CehoProxy\singbox.json", windows: true));

    [Fact]
    public void Unix_arguments_support_spaces_without_regex_matching()
    {
        const string config = "/profiles/a [private]/singbox.json";
        Assert.True(OwnedEngineProcess.Matches("sing-box", new[] { "/usr/bin/sing-box", "run", "-c", config }, config, false));
        Assert.False(OwnedEngineProcess.Matches("sing-box", new[] { "/usr/bin/sing-box", "run", "-c", config + "2" }, config, false));
        Assert.False(OwnedEngineProcess.Matches("python", new[] { "python", "run", "-c", config }, config, false));
    }

    [Fact]
    public void Guard_and_main_config_are_different_owners()
    {
        var args = new[] { "ceho-engine", "run", "-c", "/profile/guard.json" };
        Assert.True(OwnedEngineProcess.Matches("ceho-engine", args, "/profile/guard.json", false));
        Assert.False(OwnedEngineProcess.Matches("ceho-engine", args, "/profile/singbox.json", false));
    }
}

using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class CommandNameTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude-code")]
    [InlineData("tool_2.0")]
    public void Short_command_names_are_accepted(string name) =>
        Assert.True(CommandName.IsSafe(name));

    [Theory]
    [InlineData("")]
    [InlineData("../curl")]
    [InlineData("/usr/bin/curl")]
    [InlineData(@"C:\Program Files\tool.exe")]
    [InlineData("folder/tool")]
    [InlineData(@"folder\tool")]
    [InlineData("two words")]
    [InlineData(".")]
    [InlineData("..")]
    public void Paths_and_ambiguous_names_are_rejected(string name) =>
        Assert.False(CommandName.IsSafe(name));
}

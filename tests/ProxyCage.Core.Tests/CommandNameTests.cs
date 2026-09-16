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

    [Fact]
    public void Windows_npm_shims_and_native_commands_share_one_wrapper_name()
    {
        Assert.Equal("gemini.cmd", CommandName.WrapperFileName("gemini", windows: true));
        Assert.Equal("gemini.cmd", CommandName.WrapperFileName("gemini.cmd", windows: true));
        Assert.Equal(new[] { "gemini.exe", "gemini.cmd", "gemini.bat", "gemini" },
            CommandName.ExecutableCandidates("gemini.cmd", windows: true));
    }
}

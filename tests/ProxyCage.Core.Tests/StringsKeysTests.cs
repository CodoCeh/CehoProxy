namespace ProxyCage.Core.Tests;

public class StringsKeysTests
{
    [Theory]
    [InlineData("cli_ping_title")]
    [InlineData("stage_done")]
    [InlineData("stage_failed")]
    public void Keys_used_in_code_have_text_in_both_languages(string key)
    {
        foreach (var lang in Strings.Languages)
            Assert.NotEqual(key, Strings.T(lang, key, "x"));
    }
}

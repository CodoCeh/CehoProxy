using System.Text.RegularExpressions;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class CliSelectionTests
{
    [Fact]
    public void Interpreted_cli_cannot_be_misleadingly_added_as_an_application()
    {
        if (Os.IsWindows) return;

        var root = Path.Combine(Path.GetTempPath(), "ceho-script-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "gemini");
        File.WriteAllText(script, "#!/usr/bin/env node\nconsole.log('ok')\n");
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => AppDetector.Detect(script));
            Assert.Contains("chp wrap gemini", error.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Codex", "/Applications/ChatGPT.app", "/Applications/ChatGPT.app/Contents/Resources/codex")]
    [InlineData("Claude", "/opt/homebrew/lib/node_modules/@anthropic-ai/claude-code", "/opt/homebrew/lib/node_modules/@anthropic-ai/claude-code/bin/claude.exe")]
    [InlineData("Codex", "/opt/codex", "/opt/codex/bin/codex")]
    [InlineData("Codex", @"C:\Program Files\WindowsApps\OpenAI.Codex_1.2.3.0_x64__id", @"C:\Users\User\AppData\Local\OpenAI\Codex\bin\abc\codex.exe")]
    public void Native_cli_process_is_covered_by_selected_application(string name, string folder, string process)
    {
        var app = new AppEntry { Name = name, Folder = folder, Enabled = true };

        Assert.Contains(AppDetector.ToRegexes(app), rx =>
            Regex.IsMatch(process, rx, RegexOptions.CultureInvariant));
    }
}

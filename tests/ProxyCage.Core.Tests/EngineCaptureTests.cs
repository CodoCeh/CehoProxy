using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Вывод движка мы читаем сами, поэтому проверяем на настоящем процессе:
/// вместо sing-box подставляется скрипт, который говорит то же самое.
/// </summary>
[Collection("journal")]
public class EngineCaptureTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "chp-engine-" + Guid.NewGuid().ToString("N")[..8]);

    public EngineCaptureTests()
    {
        Directory.CreateDirectory(_root);
        Log.Init(_root, "test");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>Строки склеиваются вручную: с переводом строки Windows shell ломается.</summary>
    private string FakeEngine(params string[] lines)
    {
        var path = Path.Combine(_root, "fake-sing-box.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + string.Join("\n", lines) + "\n");
        Os.Run("chmod", $"755 {path}", 5000);
        return path;
    }

    private SingBoxProcess Run(params string[] lines)
    {
        var proc = new SingBoxProcess();
        proc.Start(FakeEngine(lines), Path.Combine(_root, "singbox.json"), _root);
        proc.WaitForExit();
        return proc;
    }

    private const string ToStdout = "echo \"INFO router: started\"";
    private const string ToStderr = "echo \"FATAL start service: tun already in use\" 1>&2";

    [Fact]
    public void Both_streams_of_the_engine_reach_the_journal()
    {
        if (Os.IsWindows) return;

        using var proc = Run(ToStdout, ToStderr);

        var journal = Log.Tail(50, LogView.Engine);

        Assert.Contains(journal, l => l.Contains("router: started"));
        Assert.Contains(journal, l => l.Contains("tun already in use"));
        Assert.Empty(Directory.GetFiles(_root, "sing-box.log"));
    }

    [Fact]
    public void Reason_for_the_death_names_the_engine_complaint_and_the_code()
    {
        if (Os.IsWindows) return;

        using var proc = Run(ToStdout, ToStderr, "exit 3");

        var explained = proc.Explain("ru");

        Assert.Contains("tun already in use", explained);
        Assert.Contains("3", explained);
        Assert.Equal(3, proc.ExitCode);
    }

    [Fact]
    public void Silent_death_still_gets_explained()
    {
        if (Os.IsWindows) return;

        using var proc = Run("exit 7");

        Assert.Contains("7", proc.Explain("ru"));
    }

    [Fact]
    public void Only_lines_of_the_current_run_are_kept()
    {
        if (Os.IsWindows) return;

        using var proc = Run("echo \"INFO первый запуск\"");
        Assert.Contains(proc.EngineLogOfThisRun(), l => l.Contains("первый запуск"));

        proc.Start(FakeEngine("echo \"INFO второй запуск\""), Path.Combine(_root, "singbox.json"), _root);
        proc.WaitForExit();

        var lines = proc.EngineLogOfThisRun();
        Assert.Contains(lines, l => l.Contains("второй запуск"));
        Assert.DoesNotContain(lines, l => l.Contains("первый запуск"));
    }

    [Theory]
    [InlineData("response received, protocol: h2, status: 308")]
    [InlineData("response received, protocol: h2, status: 407")]
    public void Proxy_auth_failure_gives_meaningful_hint(string logLine)
    {
        var hintRu = SingBoxProcess.Hint(logLine, "ru");
        Assert.NotNull(hintRu);
        Assert.Contains("авторизаци", hintRu);

        var hintEn = SingBoxProcess.Hint(logLine, "en");
        Assert.NotNull(hintEn);
        Assert.Contains("authentication", hintEn);
    }
}

using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Журнал общий для всей программы, поэтому тесты, которые в него пишут, идут по одному.
/// </summary>
[CollectionDefinition("journal", DisableParallelization = true)]
public class JournalCollection { }

[Collection("journal")]
public class LogTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "chp-log-" + Guid.NewGuid().ToString("N")[..8]);

    public LogTests()
    {
        Directory.CreateDirectory(_root);
        Log.Init(_root, "test");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static string Mark() => "метка-" + Guid.NewGuid().ToString("N")[..6];

    [Fact]
    public void Program_and_engine_share_one_file_on_disk()
    {
        var ours = Mark();
        var theirs = Mark();

        Log.Info(ours);
        Log.Engine("INFO router: " + theirs);

        var files = Directory.GetFiles(_root).Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "cehoproxy.log" }, files);

        var text = File.ReadAllText(Path.Combine(_root, "cehoproxy.log"));
        Assert.Contains(ours, text);
        Assert.Contains(theirs, text);
    }

    [Fact]
    public void Engine_lines_are_told_apart_from_ours()
    {
        var ours = Mark();
        var theirs = Mark();

        Log.Warn(ours);
        Log.Engine("ERROR inbound/tun: " + theirs);

        var engine = Log.Tail(50, LogView.Engine);
        var mine = Log.Tail(50, LogView.Ours);

        Assert.Contains(engine, l => l.Contains(theirs));
        Assert.DoesNotContain(engine, l => l.Contains(ours));
        Assert.Contains(mine, l => l.Contains(ours));
        Assert.DoesNotContain(mine, l => l.Contains(theirs));
    }

    [Fact]
    public void Engine_severity_is_taken_from_its_own_words()
    {
        var fatal = Mark();
        var plain = Mark();
        var debugDns = Mark();
        var ansiDebug = Mark();

        Log.Engine("FATAL start service: " + fatal);
        Log.Engine("INFO router: " + plain);
        Log.Engine("DEBUG dns: exchange A example.com rcode=NOERROR " + debugDns);
        Log.Engine("\x1b[37mDEBUG\x1b[0m[0246] router: sniff " + ansiDebug);

        var entries = Log.Entries(50, LogView.Engine);

        Assert.Equal("error", entries.Single(e => e.Message.Contains(fatal)).Level);
        Assert.Equal("info", entries.Single(e => e.Message.Contains(plain)).Level);
        Assert.Equal("debug", entries.Single(e => e.Message.Contains(debugDns)).Level);
        Assert.Equal("debug", entries.Single(e => e.Message.Contains(ansiDebug)).Level);
    }

    [Fact]
    public void Crash_lands_in_the_journal_as_a_readable_block()
    {
        var context = "проверка " + Mark();

        try { throw new InvalidOperationException("подписка не открылась"); }
        catch (Exception ex) { Log.Crash(context, ex); }

        var crash = Log.Crashes().First(c => c.Context == context);

        Assert.Contains(crash.Lines, l => l.Contains("InvalidOperationException"));
        Assert.Contains(crash.Lines, l => l.Contains("подписка не открылась"));
        Assert.Contains(crash.Lines, l => l.Contains(Updater.CurrentVersion));
        Assert.True((DateTime.Now - crash.When).TotalMinutes < 1);

        Assert.Empty(Directory.GetFiles(_root, "crash-*.log"));
    }

    [Fact]
    public void Crashes_come_back_freshest_first()
    {
        var older = "раньше " + Mark();
        var newer = "позже " + Mark();

        Log.Crash(older, new Exception("раз"));
        Log.Crash(newer, new Exception("два"));

        var contexts = Log.Crashes().Select(c => c.Context).ToList();

        Assert.True(contexts.IndexOf(newer) < contexts.IndexOf(older));
    }

    [Fact]
    public void Crash_view_shows_only_crash_lines()
    {
        var ordinary = Mark();
        Log.Info(ordinary);
        Log.Crash("падение " + Mark(), new Exception("причина"));

        var crashes = Log.Tail(80, LogView.Crashes);

        Assert.NotEmpty(crashes);
        Assert.DoesNotContain(crashes, l => l.Contains(ordinary));
        Assert.Contains(crashes, l => l.Contains("причина"));
    }

    [Fact]
    public void Clearing_leaves_one_empty_file_and_no_leftovers()
    {
        Log.Info(Mark());
        File.WriteAllText(Path.Combine(_root, "cehoproxy.log.1"), "предыдущая часть");

        Log.Clear();

        Assert.False(File.Exists(Path.Combine(_root, "cehoproxy.log.1")));
        Assert.Equal(new[] { "cehoproxy.log" },
            Directory.GetFiles(_root).Select(Path.GetFileName).ToList());
        Assert.Empty(Log.Crashes());
    }

    [Fact]
    public void Files_of_the_old_scheme_are_removed_on_start()
    {
        File.WriteAllText(Path.Combine(_root, "sing-box.log"), "лог движка от прошлой версии");
        File.WriteAllText(Path.Combine(_root, "crash-20260101-000000.log"), "падение");

        Log.Init(_root, "test");

        Assert.Equal(new[] { "cehoproxy.log" },
            Directory.GetFiles(_root).Select(Path.GetFileName).ToList());
    }

    [Fact]
    public void Lines_that_are_not_ours_do_not_break_reading()
    {
        var mark = Mark();
        File.AppendAllText(Path.Combine(_root, "cehoproxy.log"),
            "мусор без времени\n\n" + Environment.NewLine);
        Log.Info(mark);

        Assert.Contains(Log.Tail(20), l => l.Contains(mark));
    }

    [Fact]
    public void Panel_shows_newest_lines_first()
    {
        var older = "раньше-" + Mark();
        var newer = "позже-" + Mark();

        Log.Info(older);
        Log.Info(newer);

        var newestFirst = Log.TailNewestFirst(20, LogView.Ours);
        var asFile = Log.Tail(20, LogView.Ours);

        Assert.True(newestFirst.ToList().FindIndex(l => l.Contains(newer))
                    < newestFirst.ToList().FindIndex(l => l.Contains(older)));
        Assert.True(asFile.ToList().FindIndex(l => l.Contains(older))
                    < asFile.ToList().FindIndex(l => l.Contains(newer)));
    }
}

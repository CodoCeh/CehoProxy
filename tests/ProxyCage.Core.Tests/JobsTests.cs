using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("journal")]
public class JobsTests
{
    private static async Task<Job> Finished(Job job)
    {
        for (var i = 0; i < 200 && job.Running; i++) await Task.Delay(25);
        Assert.False(job.Running, "задача не закончилась за 5 секунд");
        return job;
    }

    [Fact]
    public async Task Keeps_stages_and_reaches_hundred_percent()
    {
        var job = Jobs.Start("test-stages", "начинаю", async progress =>
        {
            progress.Stage("читаю подписки", 30);
            progress.Note("подписка 1 из 2");
            progress.Stage("пишу правила", 70);
            await Task.Yield();
            return "готово: 12 нод";
        });

        await Finished(job);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(100, job.Percent);
        Assert.Equal("готово: 12 нод", job.Result);
        Assert.False(job.IsError);
        Assert.Equal(
            new[] { "начинаю", "читаю подписки", "подписка 1 из 2", "пишу правила", "готово: 12 нод" },
            job.Steps);
    }

    [Fact]
    public async Task Failure_is_visible_instead_of_disappearing()
    {
        var job = Jobs.Start("test-fail", "пробую", _ =>
            throw new InvalidOperationException("подписка не открылась"));

        await Finished(job);

        Assert.Equal(JobState.Failed, job.State);
        Assert.True(job.IsError);
        Assert.Equal("подписка не открылась", job.Result);
        Assert.NotNull(job.FinishedUtc);
    }

    [Fact]
    public async Task Second_click_joins_the_running_job_instead_of_doubling_it()
    {
        var release = new TaskCompletionSource();
        var runs = 0;

        var first = Jobs.Start("test-single", "работаю", async _ =>
        {
            Interlocked.Increment(ref runs);
            await release.Task;
            return "ок";
        });
        var second = Jobs.Start("test-single", "работаю", async _ =>
        {
            Interlocked.Increment(ref runs);
            await release.Task;
            return "ок";
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Same(first, Jobs.Active("test-single"));

        release.SetResult();
        await Finished(first);

        Assert.Equal(1, runs);
        Assert.Null(Jobs.Active("test-single"));
        Assert.Same(first, Jobs.Latest("test-single"));
    }

    [Fact]
    public async Task Rerun_if_busy_repeats_work_with_the_latest_config()
    {
        var firstGate = new TaskCompletionSource();
        var runs = 0;

        var first = Jobs.Start("test-rerun-busy", "работаю", async _ =>
        {
            var n = Interlocked.Increment(ref runs);
            if (n == 1) await firstGate.Task;
            return "круг " + n;
        }, rerunIfBusy: true);

        var second = Jobs.Start("test-rerun-busy", "работаю", async _ =>
        {
            await Task.Yield();
            Interlocked.Increment(ref runs);
            return "не должен";
        }, rerunIfBusy: true);

        Assert.Equal(first.Id, second.Id);
        firstGate.SetResult();
        await Finished(first);

        Assert.Equal(2, runs);
        Assert.Equal("круг 2", first.Result);
        Assert.Equal(JobState.Done, first.State);
    }

    [Fact]
    public async Task Percent_never_leaves_the_bar()
    {
        var seen = new List<int>();
        var job = Jobs.Start("test-clamp", "старт", async progress =>
        {
            progress.Stage("мимо", 250);
            seen.Add(Jobs.Active("test-clamp")!.Percent);
            progress.Stage("назад", -40);
            seen.Add(Jobs.Active("test-clamp")!.Percent);
            await Task.Yield();
            return "ок";
        });

        await Finished(job);

        Assert.Equal(new[] { 100, 0 }, seen);
        Assert.Equal(100, job.Percent);
    }

    [Fact]
    public async Task Repeated_stage_text_does_not_flood_the_list()
    {
        var job = Jobs.Start("test-dedup", "старт", async progress =>
        {
            progress.Note("измеряю задержку");
            progress.Note("измеряю задержку");
            progress.Note("измеряю задержку");
            await Task.Yield();
            return "ок";
        });

        await Finished(job);

        Assert.Equal(new[] { "старт", "измеряю задержку", "ок" }, job.Steps);
    }
}

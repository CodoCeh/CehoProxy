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
    public async Task Network_failure_is_an_error_not_a_crash()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Log.Init(root, "test", announce: false);
            var kind = "test-net-" + Guid.NewGuid().ToString("N")[..6];

            var job = await Finished(Jobs.Start(kind, "проверяю", _ =>
                throw new HttpRequestException("Connection reset by peer (api.github.com:443)")));

            Assert.Equal(JobState.Failed, job.State);
            Assert.DoesNotContain(Log.Crashes(), c => c.Context.Contains(kind));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
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

[Collection("journal")]
public class JobTelemetryTests
{
    [Fact]
    public async Task Exact_power_command_joins_but_conflicting_intent_is_rejected_atomically()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var kind = "power-identity-" + Guid.NewGuid();
        var first = Jobs.Start(kind, "Starting protection", async _ =>
        {
            await release.Task;
            return "Ready";
        }, operationIdentity: "start");
        try
        {
            var duplicate = Jobs.Start(kind, "Starting protection", _ => throw new Exception("Must join"),
                operationIdentity: "start");
            Assert.Same(first, duplicate);
            var conflict = Assert.Throws<JobOperationConflictException>(() =>
                Jobs.Start(kind, "Stopping protection", _ => throw new Exception("Must not run"),
                    operationIdentity: "stop"));
            Assert.Equal(first.Id, conflict.ActiveJobId);
            Assert.Equal(first.Title, conflict.ActiveTitle);
            Assert.Throws<JobOperationConflictException>(() =>
                Jobs.Start(kind, "Restarting protection", _ => throw new Exception("Must not run"),
                    operationIdentity: "restart"));
            Assert.Same(first, Jobs.Active(kind));
        }
        finally { release.TrySetResult(); }
        for (var i = 0; i < 200 && first.Running; i++) await Task.Delay(10);
        Assert.False(first.Running);
        var laterStop = Jobs.Start(kind, "Stopping protection", _ => Task.FromResult("Off"), operationIdentity: "stop");
        Assert.NotEqual(first.Id, laterStop.Id);
        for (var i = 0; i < 200 && laterStop.Running; i++) await Task.Delay(10);
        Assert.Equal(JobState.Done, laterStop.State);
    }

    [Fact]
    public async Task Racing_different_power_commands_admit_only_one_identity()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var kind = "power-conflict-race-" + Guid.NewGuid();
        var workCalls = 0;
        var calls = Enumerable.Range(0, 64).Select(n => Task.Run(async () =>
        {
            await begin.Task;
            try
            {
                return Jobs.Start(kind, n % 2 == 0 ? "Start" : "Stop", async _ =>
                {
                    Interlocked.Increment(ref workCalls);
                    await release.Task;
                    return "Done";
                }, operationIdentity: n % 2 == 0 ? "start" : "stop");
            }
            catch (JobOperationConflictException) { return null; }
        })).ToArray();
        begin.SetResult();
        var attempts = await Task.WhenAll(calls);
        var accepted = attempts.Where(j => j is not null).ToList();
        Assert.Equal(32, accepted.Count);
        Assert.Single(accepted.Select(j => j!.Id).Distinct());
        release.SetResult();
        var job = accepted[0]!;
        for (var i = 0; i < 200 && job.Running; i++) await Task.Delay(10);
        Assert.False(job.Running);
        Assert.Equal(1, workCalls);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }

    [Fact]
    public void Indeterminate_phase_reports_real_elapsed_and_idle_time_without_fake_progress()
    {
        var clock = new ManualTime();
        var job = new Job(clock) { Id = "clock", Kind = "test", Title = "test" };
        var report = new JobProgress(job);
        report.Phase("Waiting for country checks", waiting: true);
        clock.Advance(TimeSpan.FromSeconds(35));
        report.Note("3/10 checks finished");
        clock.Advance(TimeSpan.FromSeconds(4));

        var view = job.Snapshot();
        Assert.True(view.Indeterminate);
        Assert.True(view.Waiting);
        Assert.True(view.IsSlow);
        Assert.False(view.CanCancel);
        Assert.Equal(0, view.Percent);
        Assert.Equal(39, view.Seconds);
        Assert.Equal(39, view.StageSeconds);
        Assert.Equal(4, view.IdleSeconds);
        Assert.Equal("Waiting for country checks", view.Phase);
        Assert.Equal("3/10 checks finished", view.Stage);
        Assert.Equal(DateTime.UnixEpoch, view.StageStartedUtc);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(35), view.LastUpdatedUtc);
    }

    [Fact]
    public void Phase_clock_resets_only_for_new_phase_and_terminal_view_is_immutable()
    {
        var clock = new ManualTime();
        var job = new Job(clock) { Id = "clock", Kind = "test", Title = "test" };
        var report = new JobProgress(job);
        report.Phase("Loading");
        clock.Advance(TimeSpan.FromSeconds(35));
        report.Phase("Starting engine");
        clock.Advance(TimeSpan.FromSeconds(2));
        var before = job.Snapshot();
        Assert.False(before.IsSlow);
        Assert.Equal(2, before.StageSeconds);
        job.Complete("Ready");
        clock.Advance(TimeSpan.FromSeconds(100));
        report.Note("Late callback must not overwrite the result");
        var after = job.Snapshot();
        Assert.Equal(JobState.Done, after.State);
        Assert.NotNull(after.FinishedUtc);
        Assert.Equal("Ready", after.Stage);
        Assert.Equal(37, after.Seconds);
        Assert.Equal(2, after.StageSeconds);
        Assert.False(after.IsSlow);
        Assert.False(after.Indeterminate);
        Assert.Equal(100, after.Percent);
        Assert.DoesNotContain("Ready", before.Steps);
    }

    [Fact]
    public void Failed_unknown_work_is_terminal_without_claiming_successful_hundred_percent()
    {
        var job = new Job { Id = "failed", Kind = "test", Title = "test" };
        new JobProgress(job).Phase("Waiting for engine", waiting: true);
        job.Complete("Readiness timed out", failed: true);
        var view = job.Snapshot();
        Assert.Equal(JobState.Failed, view.State);
        Assert.True(view.IsError);
        Assert.Equal(0, view.Percent);
        Assert.False(view.Indeterminate);
        Assert.False(view.Waiting);
        Assert.NotNull(view.FinishedUtc);
    }

    [Fact]
    public void Step_history_is_bounded_and_detached_from_future_updates()
    {
        var job = new Job { Id = "history", Kind = "test", Title = "test" };
        var report = new JobProgress(job);
        for (var i = 0; i < 100; i++) report.Note($"Step {i}");
        var view = job.Snapshot();
        Assert.Equal(60, view.Steps.Count);
        Assert.Equal("Step 40", view.Steps[0]);
        report.Note("Next");
        Assert.Equal("Step 99", view.Steps[^1]);
    }

    [Fact]
    public async Task Concurrent_requests_have_one_admission_and_one_worker()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var kind = "race-" + Guid.NewGuid();
        var runs = 0;
        var calls = Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return Jobs.Start(kind, "Start", async progress =>
            {
                Interlocked.Increment(ref runs);
                await release.Task;
                return "Ready";
            });
        })).ToArray();
        start.SetResult();
        var jobs = await Task.WhenAll(calls);
        Assert.Single(jobs.Select(j => j.Id).Distinct());
        release.SetResult();
        for (var i = 0; i < 200 && jobs[0].Running; i++) await Task.Delay(10);
        Assert.False(jobs[0].Running);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Rerun_requests_are_not_silently_dropped_after_four_passes()
    {
        var kind = "reruns-" + Guid.NewGuid();
        var runs = 0;
        var job = Jobs.Start(kind, "Apply", async _ =>
        {
            var pass = Interlocked.Increment(ref runs);
            if (pass < 7)
                Jobs.Start(kind, "Apply", _ => throw new Exception("Must join"), rerunIfBusy: true);
            await Task.Yield();
            return $"Pass {pass}";
        });
        for (var i = 0; i < 200 && job.Running; i++) await Task.Delay(10);
        Assert.False(job.Running);
        Assert.Equal(7, runs);
        Assert.Equal("Pass 7", job.Result);
    }
}


public sealed class StartupMilestoneTests
{
    [Fact]
    public void Only_confirmed_milestones_advance_and_never_move_backwards()
    {
        var job = new Job { Id = "milestones", Kind = "power", Title = "Start" };
        var report = new JobProgress(job);
        Assert.Equal(0, job.Snapshot().StartupStep);
        report.StartupStep(1); report.StartupStep(2); report.StartupStep(1);
        Assert.Equal(2, job.Snapshot().StartupStep);
        report.StartupStep(99); Assert.Equal(2, job.Snapshot().StartupStep);
        report.StartupStep(3); job.Complete("failed", failed: true);
        report.StartupStep(1); Assert.Equal(3, job.Snapshot().StartupStep);
        Assert.True(job.Snapshot().IsError);
    }
}

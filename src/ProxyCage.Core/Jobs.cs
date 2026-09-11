using System.Collections.Concurrent;

namespace ProxyCage.Core;

public enum JobState { Running, Done, Failed }

/// <summary>
/// Долгое действие, за которым можно следить: панель показывает этап и полосу,
/// а не висит на POST-запросе до самого конца.
/// </summary>
public sealed class Job
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }

    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public DateTime? FinishedUtc { get; internal set; }

    public JobState State { get; internal set; } = JobState.Running;

    public int Percent { get; internal set; }
    public string Stage { get; internal set; } = "";
    public string? Result { get; internal set; }
    public bool IsError { get; internal set; }

    /// <summary>
    /// После успеха панель сама выйдет и поднимется заново. Браузер не перезагружает
    /// страницу сразу, а ждёт, пока интерфейс ответит.
    /// </summary>
    public bool RelaunchPanel { get; set; }

    private readonly List<string> _steps = new();

    public IReadOnlyList<string> Steps
    {
        get { lock (_steps) return _steps.ToList(); }
    }

    public bool Running => State == JobState.Running;

    public TimeSpan Elapsed => (FinishedUtc ?? DateTime.UtcNow) - StartedUtc;

    private int _rerun;

    /// <summary>
    /// Пока задача идёт, список программ мог измениться. Следующий круг возьмёт уже новый конфиг.
    /// </summary>
    internal void RequestRerun() => Interlocked.Exchange(ref _rerun, 1);

    internal bool TakeRerun() => Interlocked.Exchange(ref _rerun, 0) != 0;

    internal void AddStep(string text)
    {
        lock (_steps)
        {
            if (_steps.Count > 0 && _steps[^1] == text) return;
            _steps.Add(text);
            if (_steps.Count > 60) _steps.RemoveAt(0);
        }
    }
}

/// <summary>Куда долгая операция рассказывает, на каком она этапе: в панель или в терминал.</summary>
public interface IStageReport
{
    void Stage(string text, int percent);
    void Note(string text);
}

public sealed class DelegateReport : IStageReport
{
    private readonly Action<string> _onText;

    public DelegateReport(Action<string> onText) => _onText = onText;

    public void Stage(string text, int percent) => _onText(text);

    public void Note(string text) => _onText(text);
}

public sealed class JobProgress : IStageReport
{
    private readonly Job _job;

    internal JobProgress(Job job) => _job = job;

    /// <summary>Новый этап: и текст, и доля выполненного, чтобы полоса двигалась осмысленно.</summary>
    public void Stage(string text, int percent)
    {
        _job.Percent = Math.Clamp(percent, 0, 100);
        _job.Stage = text;
        _job.AddStep(text);
        Log.Info($"[{_job.Kind}] {percent}% {text}");
    }

    /// <summary>Уточнение внутри этапа: полосу не двигает.</summary>
    public void Note(string text)
    {
        _job.Stage = text;
        _job.AddStep(text);
        Log.Info($"[{_job.Kind}] {text}");
    }
}

public static class Jobs
{
    private static readonly ConcurrentDictionary<string, Job> All = new();
    private static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(10);
    private static long _counter;

    /// <summary>
    /// Запускает действие в фоне. Если такое же уже идёт, возвращает его же:
    /// две загрузки подписок или два запуска туннеля одновременно ничего хорошего не дают.
    /// <paramref name="rerunIfBusy"/> — после текущего круга повторить работу:
    /// список программ мог измениться, пока шли подписки.
    /// </summary>
    public static Job Start(
        string kind, string title, Func<JobProgress, Task<string>> work, bool rerunIfBusy = false)
    {
        Forget();

        if (Active(kind) is { } already)
        {
            if (rerunIfBusy) already.RequestRerun();
            return already;
        }

        var job = new Job
        {
            Id = $"{kind}-{Interlocked.Increment(ref _counter)}",
            Kind = kind,
            Title = title,
        };
        All[job.Id] = job;

        var progress = new JobProgress(job);
        progress.Stage(title, 0);

        _ = Task.Run(async () =>
        {
            try
            {
                var extra = 0;
                string result;
                while (true)
                {
                    result = await work(progress);
                    if (!job.TakeRerun() || extra++ >= 3) break;
                    progress.Stage(title, 0);
                }

                job.Percent = 100;
                job.Result = result;
                job.State = JobState.Done;
                job.Stage = result;
                job.AddStep(result);
                Log.Info($"[{kind}] готово за {job.Elapsed.TotalSeconds:F1} с: {result}");
            }
            catch (Exception ex)
            {
                job.Result = ex.Message;
                job.IsError = true;
                job.State = JobState.Failed;
                job.Stage = ex.Message;
                job.AddStep(ex.Message);
                Log.Error($"[{kind}] не удалось", ex);
                if (ex is not InvalidOperationException) Log.Crash($"фоновое действие {kind}", ex);
            }
            finally
            {
                job.FinishedUtc = DateTime.UtcNow;
            }
        });

        return job;
    }

    public static Job? Find(string? id) =>
        id is not null && All.TryGetValue(id, out var job) ? job : null;

    public static Job? Active(string kind) =>
        All.Values.Where(j => j.Kind == kind && j.Running)
            .OrderByDescending(j => j.StartedUtc)
            .FirstOrDefault();

    public static Job? Latest(string kind) =>
        All.Values.Where(j => j.Kind == kind)
            .OrderByDescending(j => j.StartedUtc)
            .FirstOrDefault();

    public static IReadOnlyList<Job> Running() =>
        All.Values.Where(j => j.Running).OrderBy(j => j.StartedUtc).ToList();

    private static void Forget()
    {
        var deadline = DateTime.UtcNow - KeepFinished;
        foreach (var job in All.Values)
            if (job.FinishedUtc is { } finished && finished < deadline)
                All.TryRemove(job.Id, out _);
    }
}

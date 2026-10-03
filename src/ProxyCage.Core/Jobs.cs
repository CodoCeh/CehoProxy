namespace ProxyCage.Core;

public enum JobState { Running, Done, Failed }

/// <summary>A different command cannot be silently absorbed by an active operation.</summary>
public sealed class JobOperationConflictException : InvalidOperationException
{
    public string ActiveJobId { get; }
    public string ActiveTitle { get; }
    public JobOperationConflictException(Job active)
        : base($"Another operation is still running: {active.Title}. The new command was not started.")
        => (ActiveJobId, ActiveTitle) = (active.Id, active.Title);
}

/// <summary>A coherent view of an operation, safe to serialize while it is reporting progress.</summary>
public sealed record JobSnapshot(
    string Id, string Kind, string Title, JobState State, int Percent, string Stage,
    string? Result, bool IsError, bool RelaunchPanel, DateTime StartedUtc, DateTime? FinishedUtc,
    string Phase, DateTime StageStartedUtc, DateTime LastUpdatedUtc, bool Indeterminate, bool Waiting,
    double Seconds, double StageSeconds, double IdleSeconds, bool IsSlow, bool CanCancel,
    IReadOnlyList<string> Steps, int StartupStep = 0, long Revision = 0);

/// <summary>
/// A background operation. Unknown work is indeterminate; elapsed time is observation,
/// not a synthetic percentage or a promise that an operation has hung.
/// </summary>
public sealed class Job
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    internal string? OperationIdentity { get; init; }

    private readonly object _sync = new();
    private readonly List<string> _steps = new();
    private readonly TimeProvider _clock;
    private readonly long _startedTimestamp;
    private long _stageTimestamp, _updatedTimestamp;
    private long? _finishedTimestamp;
    private DateTime? _finishedUtc;
    private DateTime _stageStartedUtc, _lastUpdatedUtc;
    private JobState _state;
    private int _percent, _startupStep;
    private long _revision;
    private string _stage = "", _phase = "";
    private string? _result;
    private bool _isError, _relaunchPanel, _rerun;
    private bool _indeterminate = true, _waiting;

    public Job() : this(TimeProvider.System) { }
    internal Job(TimeProvider clock)
    {
        _clock = clock;
        StartedUtc = _stageStartedUtc = _lastUpdatedUtc = clock.GetUtcNow().UtcDateTime;
        _startedTimestamp = _stageTimestamp = _updatedTimestamp = clock.GetTimestamp();
    }

    public DateTime StartedUtc { get; }
    public DateTime? FinishedUtc { get { lock (_sync) return _finishedUtc; } }
    public JobState State { get { lock (_sync) return _state; } }
    public int Percent { get { lock (_sync) return _percent; } }
    public string Stage { get { lock (_sync) return _stage; } }
    public string? Result { get { lock (_sync) return _result; } }
    public bool IsError { get { lock (_sync) return _isError; } }
    public bool RelaunchPanel
    {
        get { lock (_sync) return _relaunchPanel; }
        set { lock (_sync) _relaunchPanel = value; }
    }
    public IReadOnlyList<string> Steps { get { lock (_sync) return _steps.ToArray(); } }
    public bool Running => State == JobState.Running;
    public TimeSpan Elapsed { get { lock (_sync) return _clock.GetElapsedTime(_startedTimestamp, _finishedTimestamp ?? _clock.GetTimestamp()); } }
    public bool Indeterminate { get { lock (_sync) return _indeterminate; } }
    public DateTime StageStartedUtc { get { lock (_sync) return _stageStartedUtc; } }
    public DateTime LastUpdatedUtc { get { lock (_sync) return _lastUpdatedUtc; } }

    public JobSnapshot Snapshot()
    {
        lock (_sync)
        {
            var now = _finishedTimestamp ?? _clock.GetTimestamp();
            var phaseSeconds = Math.Max(0, _clock.GetElapsedTime(_stageTimestamp, now).TotalSeconds);
            return new(Id, Kind, Title, _state, _percent, _stage, _result, _isError, _relaunchPanel,
                StartedUtc, _finishedUtc, _phase, _stageStartedUtc, _lastUpdatedUtc, _indeterminate, _waiting,
                Math.Max(0, _clock.GetElapsedTime(_startedTimestamp, now).TotalSeconds), phaseSeconds,
                Math.Max(0, _clock.GetElapsedTime(_updatedTimestamp, now).TotalSeconds),
                _state == JobState.Running && phaseSeconds >= 30, false, _steps.ToArray(), _startupStep, _revision);
        }
    }

    internal void SetStartupStep(int step)
    {
        lock (_sync)
        {
            if (_state == JobState.Running && step is >= 1 and <= 3 && step > _startupStep)
            { _startupStep = step; _revision++; }
        }
    }

    internal void RequestRerun() { lock (_sync) _rerun = true; }
    internal bool TakeRerun() { lock (_sync) { var rerun = _rerun; _rerun = false; return rerun; } }

    internal void Report(string text, int? percent, bool newPhase, bool waiting = false)
    {
        lock (_sync)
        {
            if (_state != JobState.Running) return;
            _revision++;
            var now = _clock.GetUtcNow().UtcDateTime;
            var timestamp = _clock.GetTimestamp();
            if (newPhase)
            {
                _phase = text;
                _stageStartedUtc = now;
                _stageTimestamp = timestamp;
                _indeterminate = percent is null;
                _waiting = waiting;
            }
            if (percent is { } value) _percent = Math.Clamp(value, 0, 100);
            _stage = text;
            _lastUpdatedUtc = now;
            _updatedTimestamp = timestamp;
            AddStep(text);
        }
    }

    internal void Complete(string result, bool failed = false)
    {
        lock (_sync)
        {
            if (_state != JobState.Running) return;
            _revision++;
            _finishedUtc = _lastUpdatedUtc = _clock.GetUtcNow().UtcDateTime;
            _finishedTimestamp = _updatedTimestamp = _clock.GetTimestamp();
            _result = _stage = result;
            _isError = failed;
            _indeterminate = _waiting = false;
            if (!failed) _percent = 100;
            AddStep(result);
            // Publish terminal state only once result and finish time are available.
            _state = failed ? JobState.Failed : JobState.Done;
        }
    }

    private void AddStep(string text)
    {
        if (_steps.Count > 0 && _steps[^1] == text) return;
        _steps.Add(text);
        if (_steps.Count > 60) _steps.RemoveAt(0);
    }
}

public interface IStageReport
{
    void Stage(string text, int percent);
    void Note(string text);
    /// <summary>An operation with no reliable denominator or duration estimate.</summary>
    void Phase(string text, bool waiting = false) => Note(text);
    /// <summary>Confirmed startup milestone: preparation, connection, verification. Never a percentage.</summary>
    void StartupStep(int step) { }
}

public sealed class DelegateReport : IStageReport
{
    private readonly Action<string> _onText;
    public DelegateReport(Action<string> onText) => _onText = onText;
    public void Stage(string text, int percent) => _onText(text);
    public void Note(string text) => _onText(text);
    public void Phase(string text, bool waiting = false) => _onText(text);
}

public sealed class JobProgress : IStageReport
{
    private readonly Job _job;
    internal JobProgress(Job job) => _job = job;
    public string JobId => _job.Id;
    public void StartupStep(int step) => _job.SetStartupStep(step);

    public void Stage(string text, int percent)
    {
        _job.Report(text, percent, newPhase: true);
        Log.Info($"[{_job.Kind}] {Math.Clamp(percent, 0, 100)}% {text}");
    }
    public void Phase(string text, bool waiting = false)
    {
        _job.Report(text, null, newPhase: true, waiting);
        Log.Info($"[{_job.Kind}] {text}");
    }
    public void Note(string text)
    {
        _job.Report(text, null, newPhase: false);
        Log.Info($"[{_job.Kind}] {text}");
    }
}

public static class Jobs
{
    // Admission and finishing share one gate: a concurrent click either requests
    // another pass on the live operation or starts a new one after it is complete.
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Job> All = new();
    private static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(10);
    private static long _counter;
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..12];

    public static Job Start(
        string kind, string title, Func<JobProgress, Task<string>> work, bool rerunIfBusy = false,
        string? operationIdentity = null)
    {
        Job job;
        lock (Gate)
        {
            Forget();
            if (All.Values.FirstOrDefault(j => j.Kind == kind && j.Running) is { } already)
            {
                if (!string.Equals(already.OperationIdentity, operationIdentity, StringComparison.Ordinal))
                    throw new JobOperationConflictException(already);
                if (rerunIfBusy) already.RequestRerun();
                return already;
            }
            job = new Job { Id = $"{kind}-{SessionId}-{++_counter}", Kind = kind, Title = title,
                OperationIdentity = operationIdentity };
            job.Report(title, null, newPhase: true);
            All.Add(job.Id, job);
        }

        var progress = new JobProgress(job);
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var result = await work(progress);
                    lock (Gate)
                    {
                        if (!job.TakeRerun())
                        {
                            job.Complete(result);
                            break;
                        }
                    }
                    // Coalesce changes, but never silently discard the final config.
                    progress.Phase(title);
                }
                Log.Info($"[{kind}] готово за {job.Elapsed.TotalSeconds:F1} с: {job.Result}");
            }
            catch (Exception ex)
            {
                lock (Gate) job.Complete(ex.Message, failed: true);
                Log.Error($"[{kind}] не удалось", ex);
                if (ex is not (InvalidOperationException or HttpRequestException or OperationCanceledException or IOException or TimeoutException))
                    Log.Crash($"фоновое действие {kind}", ex);
            }
        });
        return job;
    }

    public static Job? Find(string? id)
    {
        lock (Gate) return id is not null && All.TryGetValue(id, out var job) ? job : null;
    }
    public static Job? Active(string kind)
    {
        lock (Gate) return All.Values.FirstOrDefault(j => j.Kind == kind && j.Running);
    }
    public static Job? Latest(string kind)
    {
        lock (Gate) return All.Values.Where(j => j.Kind == kind).OrderByDescending(j => j.StartedUtc).FirstOrDefault();
    }
    public static IReadOnlyList<Job> Running()
    {
        lock (Gate) return All.Values.Where(j => j.Running).OrderBy(j => j.StartedUtc).ToList();
    }
    private static void Forget()
    {
        var deadline = DateTime.UtcNow - KeepFinished;
        foreach (var job in All.Values.Where(j => j.FinishedUtc is { } at && at < deadline).ToList())
            All.Remove(job.Id);
    }
}

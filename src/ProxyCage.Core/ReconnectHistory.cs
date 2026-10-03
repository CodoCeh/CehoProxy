namespace ProxyCage.Core;

public sealed record ReconnectEntry(
    long Id, string ReasonCode, string Reason, DateTime StartedUtc, DateTime? FinishedUtc,
    string State, int Attempt, int? MaxAttempts, string Stage, string? Result);
public sealed record ReconnectSnapshot(ReconnectEntry? Current, IReadOnlyList<ReconnectEntry> Recent);

/// <summary>In-memory, bounded connection history. It records facts, never inferred network diagnoses.</summary>
public sealed class ReconnectHistory
{
    public static ReconnectHistory Shared { get; } = new();
    private readonly object _sync = new();
    private readonly List<ReconnectEntry> _entries = new();
    private readonly int _capacity;
    private long _id;

    public ReconnectHistory(int capacity = 20)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public Attempt Begin(string reasonCode, string reason, IStageReport? report = null)
    {
        lock (_sync)
        {
            var entry = new ReconnectEntry(++_id, reasonCode, reason, DateTime.UtcNow, null,
                "running", 1, null, reason, null);
            _entries.Add(entry);
            if (_entries.Count > _capacity) _entries.RemoveAt(0);
            return new Attempt(this, entry.Id, report);
        }
    }

    public ReconnectSnapshot Snapshot()
    {
        lock (_sync)
        {
            var ordered = _entries.AsEnumerable().Reverse().ToArray();
            return new(ordered.FirstOrDefault(e => e.State == "running"), ordered);
        }
    }

    private void Change(long id, Func<ReconnectEntry, ReconnectEntry> update)
    {
        lock (_sync)
        {
            var index = _entries.FindIndex(e => e.Id == id);
            if (index >= 0 && _entries[index].State == "running") _entries[index] = update(_entries[index]);
        }
    }

    public sealed class Attempt : IStageReport
    {
        private readonly ReconnectHistory _owner;
        private readonly long _id;
        private readonly IStageReport? _report;
        internal Attempt(ReconnectHistory owner, long id, IStageReport? report)
            => (_owner, _id, _report) = (owner, id, report);

        public void Retrying(int attempt, int maxAttempts, string reason)
        {
            if (attempt < 1 || maxAttempts < attempt) throw new ArgumentOutOfRangeException(nameof(attempt));
            _owner.Change(_id, e => e with { Attempt = attempt, MaxAttempts = maxAttempts, Stage = reason });
            _report?.Phase(reason, waiting: true);
        }
        public void Complete(bool succeeded, string result) =>
            _owner.Change(_id, e => e with
            {
                State = succeeded ? "succeeded" : "failed", Result = result,
                Stage = result, FinishedUtc = DateTime.UtcNow,
            });
        public void StartupStep(int step) => _report?.StartupStep(step);
        public void Stage(string text, int percent)
        {
            _owner.Change(_id, e => e with { Stage = text });
            _report?.Stage(text, percent);
        }
        public void Note(string text)
        {
            _owner.Change(_id, e => e with { Stage = text });
            _report?.Note(text);
        }
        public void Phase(string text, bool waiting = false)
        {
            _owner.Change(_id, e => e with { Stage = text });
            _report?.Phase(text, waiting);
        }
    }
}

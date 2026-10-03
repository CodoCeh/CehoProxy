namespace ProxyCage.Core.Tests;

/// <summary>
/// Drives TimeProvider timers only when the operation has actually scheduled them.
/// Await continuations may run on any thread; a slow runner never advances virtual
/// time just because a continuation has not received its turn yet.
/// </summary>
internal sealed class ManualTimerTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly HashSet<ManualTimer> _timers = new();
    private TaskCompletionSource _changed = NewSignal();
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (_sync) return _ticks; }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    public TimeSpan Elapsed => TimeSpan.FromTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public async Task<T> RunToCompletionAsync<T>(Task<T> operation, int maxTimerCallbacks = 1000)
    {
        var callbacks = 0;
        while (!operation.IsCompleted)
        {
            ManualTimer? next;
            Task changed;
            lock (_sync)
            {
                next = _timers.Where(t => t.DueAt is not null).MinBy(t => t.DueAt);
                changed = _changed.Task;
                if (next is not null)
                {
                    _ticks = next.DueAt!.Value;
                    next.DueAt = next.Period > 0 ? _ticks + next.Period : null;
                }
            }
            if (next is null)
            {
                // A continuation will either finish the operation or register its
                // next delay. Neither outcome depends on a real-time test deadline.
                await Task.WhenAny(operation, changed);
                continue;
            }
            if (++callbacks > maxTimerCallbacks)
                throw new InvalidOperationException("The operation exceeded the expected number of virtual timer callbacks.");
            next.Invoke();
        }
        return await operation;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalChanged()
    {
        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimerTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private bool _disposed;
        internal long? DueAt { get; set; }
        internal long Period { get; private set; }

        internal ManualTimer(ManualTimerTimeProvider owner, TimerCallback callback, object? state)
            => (_owner, _callback, _state) = (owner, callback, state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(period));
            lock (_owner._sync)
            {
                if (_disposed) return false;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._ticks + dueTime.Ticks;
                Period = period.Ticks;
                _owner._timers.Add(this);
                _owner.SignalChanged();
                return true;
            }
        }

        internal void Invoke()
        {
            lock (_owner._sync)
                if (_disposed) return;
            _callback(_state);
        }

        public void Dispose()
        {
            lock (_owner._sync)
            {
                if (_disposed) return;
                _disposed = true;
                _owner._timers.Remove(this);
                _owner.SignalChanged();
            }
        }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

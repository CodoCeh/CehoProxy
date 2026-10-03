namespace ProxyCage.Core;

/// <summary>
/// Bounded, monotonic recovery scheduling. The engine queue owns this policy;
/// generations also reject observations taken before a user stop or newer start.
/// A successful launch does not reset the budget: a flapping engine must settle first.
/// </summary>
public sealed class RecoveryPolicy
{
    public sealed record Request(long Generation, string ReasonCode, string Reason, long DueAt);
    private readonly long[] _delays;
    private readonly long _stableMilliseconds;
    private long? _healthySince;
    public long Generation { get; private set; }
    public bool Wanted { get; private set; }
    public int Attempts { get; private set; }
    public int MaxAttempts => _delays.Length;
    public bool Exhausted { get; private set; }
    public Request? Pending { get; private set; }

    public RecoveryPolicy(IEnumerable<TimeSpan>? delays = null, TimeSpan? stablePeriod = null)
    {
        _delays = (delays ?? new[] { 5, 15, 30, 60, 120 }.Select(seconds => TimeSpan.FromSeconds(seconds)))
            .Select(d => checked((long)d.TotalMilliseconds)).ToArray();
        _stableMilliseconds = checked((long)(stablePeriod ?? TimeSpan.FromMinutes(5)).TotalMilliseconds);
        if (_delays.Length == 0 || _delays.Any(d => d <= 0) || _stableMilliseconds <= 0
            || !_delays.SequenceEqual(_delays.Order()))
            throw new ArgumentOutOfRangeException(nameof(delays));
    }

    public void StartByUser()
    {
        Generation++;
        Wanted = true;
        Attempts = 0;
        Exhausted = false;
        Pending = null;
        _healthySince = null;
    }

    public void StopByUser()
    {
        Generation++;
        Wanted = false;
        Pending = null;
        Exhausted = false;
        _healthySince = null;
    }

    public bool IsCurrent(long generation) => Wanted && Generation == generation;

    public void Started(long now, bool healthy = true)
    {
        Pending = null;
        _healthySince = healthy ? now : null;
    }

    public void ObserveHealthy(long now)
    {
        _healthySince ??= now;
        // Bank a completed stable interval before a later negative observation
        // clears it. Otherwise separate real outages accumulate for all uptime.
        if (now - _healthySince.Value >= _stableMilliseconds) Attempts = 0;
    }
    public void ObserveUnhealthy() => _healthySince = null;

    /// <returns>True only when a new attempt was scheduled.</returns>
    public bool Schedule(long generation, string reasonCode, string reason, long now)
    {
        if (!IsCurrent(generation) || Exhausted || Pending is not null) return false;
        if (_healthySince is { } healthy && now - healthy >= _stableMilliseconds) Attempts = 0;
        _healthySince = null;
        if (Attempts >= MaxAttempts)
        {
            Exhausted = true;
            return false;
        }
        Pending = new Request(generation, reasonCode, reason, checked(now + _delays[Attempts]));
        return true;
    }

    public bool TryBegin(long now, out Request? request)
    {
        request = Pending;
        if (request is null || !IsCurrent(request.Generation) || now < request.DueAt)
        {
            request = null;
            return false;
        }
        Pending = null;
        Attempts++;
        Generation++;
        request = request with { Generation = Generation };
        return true;
    }

    public static string TerminalMessage(string language, int attempts, string reason) => language == "ru"
        ? $"Автовосстановление остановлено после {attempts} попыток. Причина: {reason} Проверьте диагностику и нажмите «Включить» для новой попытки."
        : $"Automatic recovery stopped after {attempts} attempts. Reason: {reason} Check diagnostics, then select Start to try again.";
}

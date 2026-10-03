namespace ProxyCage.Core;

public enum EngineReadinessResult { Ready, ProcessExited, TimedOut }

/// <summary>
/// A living child alone is not a ready tunnel. All required local listeners must
/// stay available for the settling interval, within a monotonic startup deadline.
/// </summary>
public static class EngineReadiness
{
    public static async Task<EngineReadinessResult> WaitAsync(
        Func<bool> isRunning, Func<Task<bool>> portsReady,
        TimeSpan? timeout = null, TimeSpan? settle = null, TimeSpan? poll = null,
        TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var limit = timeout ?? TimeSpan.FromSeconds(20);
        var stable = settle ?? TimeSpan.FromMilliseconds(1500);
        var interval = poll ?? TimeSpan.FromMilliseconds(250);
        if (limit <= TimeSpan.Zero || stable < TimeSpan.Zero || interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var began = clock.GetTimestamp();
        long? readyAt = null;
        while (true)
        {
            if (!isRunning()) return EngineReadinessResult.ProcessExited;
            var remaining = limit - clock.GetElapsedTime(began);
            if (remaining <= TimeSpan.Zero) return EngineReadinessResult.TimedOut;
            bool ready;
            try { ready = await portsReady().WaitAsync(remaining, clock); }
            catch (TimeoutException) { return EngineReadinessResult.TimedOut; }
            if (!isRunning()) return EngineReadinessResult.ProcessExited;
            // A probe that only completes after the deadline is not a successful start.
            if (clock.GetElapsedTime(began) >= limit) return EngineReadinessResult.TimedOut;
            if (ready)
            {
                readyAt ??= clock.GetTimestamp();
                if (clock.GetElapsedTime(readyAt.Value) >= stable) return EngineReadinessResult.Ready;
            }
            else readyAt = null;
            remaining = limit - clock.GetElapsedTime(began);
            if (remaining <= TimeSpan.Zero) return EngineReadinessResult.TimedOut;
            await Task.Delay(remaining < interval ? remaining : interval, clock);
        }
    }
}

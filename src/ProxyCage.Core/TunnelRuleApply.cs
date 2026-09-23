namespace ProxyCage.Core;

public static class TunnelRuleApply
{
    public static async Task<string> RunAsync(
        IStageReport report,
        Func<IDisposable> enterEngineQueue,
        Func<bool> isRunning,
        Func<IStageReport, Task<string?>> restartLocked,
        Func<IStageReport, Task<string>> apply,
        string restartedMessage)
    {
        using var gate = enterEngineQueue();
        // Запуск мог закончиться, пока изменение правил ожидало очередь движка.
        if (!isRunning()) return await apply(report);

        var error = await restartLocked(report);
        if (error is not null) throw new InvalidOperationException(error);
        return restartedMessage;
    }
}

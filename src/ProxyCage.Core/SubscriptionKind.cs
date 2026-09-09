namespace ProxyCage.Core;

public static class SubscriptionKind
{
    public static bool IsNaive(SubscriptionEntry sub) =>
        sub.Url.TrimStart().StartsWith("naive://", StringComparison.OrdinalIgnoreCase);
}

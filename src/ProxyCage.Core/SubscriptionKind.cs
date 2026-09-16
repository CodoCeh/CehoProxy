namespace ProxyCage.Core;

public static class SubscriptionKind
{
    public static bool IsNaive(SubscriptionEntry sub) =>
        NaiveProxyHelper.IsNaiveUri(sub.Url);
}

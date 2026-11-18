namespace ProxyCage.Core;

public static class EngineHttp
{
    public static HttpClient Create(TimeSpan timeout) =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.Zero }) { Timeout = timeout };
}

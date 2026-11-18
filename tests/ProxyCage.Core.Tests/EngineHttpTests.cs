using System.Net;
using System.Net.Sockets;
using System.Text;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("Unobserved tasks")]
public class EngineHttpTests
{
    [Fact]
    public async Task An_engine_restart_leaves_no_unobserved_errors_behind()
    {
        var unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> count = (_, e) => { Interlocked.Increment(ref unobserved); e.SetObserved(); };
        TaskScheduler.UnobservedTaskException += count;
        try
        {
            using var http = EngineHttp.Create(TimeSpan.FromSeconds(3));
            for (var round = 0; round < 3; round++)
            {
                var engine = new TcpListener(IPAddress.Loopback, 0);
                engine.Start();
                var port = ((IPEndPoint)engine.LocalEndpoint).Port;
                var answer = Task.Run(async () =>
                {
                    using var socket = await engine.AcceptSocketAsync();
                    await socket.ReceiveAsync(new byte[4096]);
                    await socket.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: keep-alive\r\n\r\nok"));
                    await Task.Delay(300);
                    socket.LingerState = new LingerOption(true, 0);
                    socket.Close();
                });
                Assert.Equal("ok", await http.GetStringAsync($"http://127.0.0.1:{port}/"));
                await answer;
                engine.Stop();
                await Task.Delay(200);
                await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync($"http://127.0.0.1:{port}/"));
            }
            for (var i = 0; i < 5; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(100); }
            Assert.Equal(0, unobserved);
        }
        finally { TaskScheduler.UnobservedTaskException -= count; }
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxyCage.Core;

public sealed class SingBoxNodeExitIpProbe : INodeExitIpProbe
{
    private readonly string _enginePath;
    private readonly string _workingRoot;

    public SingBoxNodeExitIpProbe(string enginePath, string workingRoot)
    {
        _enginePath = enginePath;
        _workingRoot = workingRoot;
    }

    public async Task<IPAddress?> ProbeAsync(ProxyNode node, CancellationToken cancellationToken)
    {
        var port = ReservePort();
        var probeRoot = Path.Combine(_workingRoot, "geo-probes");
        Directory.CreateDirectory(probeRoot);
        var configPath = Path.Combine(probeRoot, Guid.NewGuid().ToString("N") + ".json");
        Process? engine = null;
        try
        {
            var config = BuildConfig(node, port);
            await File.WriteAllTextAsync(configPath, config, cancellationToken);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(configPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var check = await RunEngineAsync("check", configPath, TimeSpan.FromSeconds(8), cancellationToken);
            if (check.ExitCode != 0) return null;

            engine = StartEngine(configPath);
            if (!await WaitForPortAsync(port, engine, TimeSpan.FromSeconds(6), cancellationToken)) return null;

            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                UseProxy = true,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(4),
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
            using var response = await client.GetAsync("https://api.ipify.org", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            return IPAddress.TryParse(text, out var address) ? address : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
        finally
        {
            if (engine is not null)
            {
                try { if (!engine.HasExited) engine.Kill(entireProcessTree: true); } catch { }
                try { await engine.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
                engine.Dispose();
            }
            try { File.Delete(configPath); } catch { }
        }
    }

    private Process StartEngine(string configPath)
    {
        var start = new ProcessStartInfo(_enginePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workingRoot,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(configPath);
        var process = Process.Start(start) ?? throw new InvalidOperationException("sing-box did not start");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    private async Task<(int ExitCode, string Output)> RunEngineAsync(
        string command, string configPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(_enginePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workingRoot,
        };
        start.ArgumentList.Add(command);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(configPath);
        using var process = Process.Start(start);
        if (process is null) return (-1, "");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
            return (process.ExitCode, (await stdout) + (await stderr));
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static async Task<bool> WaitForPortAsync(
        int port, Process engine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (engine.HasExited) return false;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return true;
            }
            catch (SocketException) { await Task.Delay(100, cancellationToken); }
        }
        return false;
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    internal static string BuildConfig(ProxyNode node, int port)
    {
        var outbound = OutboundBuilder.Build(node);
        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "error", ["timestamp"] = true },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject { ["type"] = "udp", ["tag"] = "dns-direct", ["server"] = "1.1.1.1", ["detour"] = "direct" },
                },
                ["final"] = "dns-direct",
                ["strategy"] = "prefer_ipv4",
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject { ["type"] = "mixed", ["tag"] = "probe-in", ["listen"] = "127.0.0.1", ["listen_port"] = port },
            },
            ["outbounds"] = new JsonArray
            {
                outbound,
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
            },
            ["route"] = new JsonObject { ["final"] = node.Tag, ["auto_detect_interface"] = true },
        };
        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}

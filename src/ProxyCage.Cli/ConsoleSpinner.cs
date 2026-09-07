using System.Diagnostics;
using ProxyCage.Core;

namespace ProxyCage.Cli;

public sealed class ConsoleSpinner : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private volatile string _currentStatus;
    private readonly bool _cursorWasVisible = true;
    private bool _done;

    private static readonly string[] Frames = Os.IsWindows
        ? new[] { "|", "/", "-", "\\" }
        : new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    public ConsoleSpinner(string initialLabel)
    {
        _currentStatus = initialLabel;

        if (!Console.IsOutputRedirected)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _cursorWasVisible = Console.CursorVisible;
                    Console.CursorVisible = false;
                }
                catch { }
            }

            _task = Task.Run(async () =>
            {
                var i = 0;
                while (!_cts.Token.IsCancellationRequested)
                {
                    var frame = Frames[i++ % Frames.Length];
                    var sec = _sw.Elapsed.Seconds;
                    var text = $"  {frame} {_currentStatus} ({sec}с)";
                    try
                    {
                        var width = 79;
                        try { if (Console.WindowWidth > 1) width = Math.Min(Console.WindowWidth - 1, 79); }
                        catch { }
                        var padded = text.Length > width ? text[..width] : text.PadRight(width);
                        Console.Write("\r" + padded);
                        await Task.Delay(100, _cts.Token);
                    }
                    catch { break; }
                }
            });
        }
        else
        {
            Console.WriteLine("  " + initialLabel);
            _task = Task.CompletedTask;
        }
    }

    public void Update(string status)
    {
        _currentStatus = status;
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine("  " + status);
        }
    }

    public void Done(string message)
    {
        if (_done) return;
        _done = true;
        Stop();
        if (!Console.IsOutputRedirected)
        {
            try { Console.Write("\r" + new string(' ', 79) + "\r"); } catch { }
        }
        Console.WriteLine("  " + message);
    }

    private void Stop()
    {
        _cts.Cancel();
        try { _task.Wait(150); } catch { }
        _cts.Dispose();
        if (!Console.IsOutputRedirected && OperatingSystem.IsWindows())
        {
            try { Console.CursorVisible = _cursorWasVisible; } catch { }
        }
    }

    public void Dispose()
    {
        if (!_done) Done(_currentStatus);
        Stop();
    }
}

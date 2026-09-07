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
    private bool _stopped;
    private volatile int _percent = -1;

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
                    var text = $"  {frame} {Bar(_percent)}{_currentStatus} ({sec}с)";
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

    private static string Bar(int percent)
    {
        if (percent < 0) return "";

        const int width = 10;
        var filled = Math.Clamp(percent * width / 100, 0, width);
        var glyphs = Os.IsWindows
            ? new string('#', filled) + new string('.', width - filled)
            : new string('█', filled) + new string('░', width - filled);
        return $"[{glyphs}] {percent,3}%  ";
    }

    public void Update(string status)
    {
        _currentStatus = status;
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine("  " + status);
        }
    }

    public void Update(string status, int percent)
    {
        _percent = Math.Clamp(percent, 0, 100);
        _currentStatus = status;
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine($"  {percent,3}%  {status}");
        }
    }

    /// <summary>Тот же способ рассказывать об этапах, что и у панели.</summary>
    public IStageReport AsReport() => new SpinnerReport(this);

    private sealed class SpinnerReport : IStageReport
    {
        private readonly ConsoleSpinner _spinner;

        public SpinnerReport(ConsoleSpinner spinner) => _spinner = spinner;

        public void Stage(string text, int percent) => _spinner.Update(text, percent);

        public void Note(string text) => _spinner.Update(text);
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
        // Done и Dispose оба гасят спиннер: второй заход не должен ронять команду.
        if (_stopped) return;
        _stopped = true;

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

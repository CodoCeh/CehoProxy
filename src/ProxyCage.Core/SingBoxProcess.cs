using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ProxyCage.Core;

public sealed class SingBoxProcess : IDisposable
{
    private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CTRL_BREAK_EVENT = 1;
    private const uint INFINITE = 0xFFFFFFFF;
    private const int SIGTERM = 15;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe, out IntPtr hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeFileHandle hObject, int dwMask, int dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int PosixKill(int pid, int sig);

    private const int KeepEngineLines = 200;

    private IntPtr _hProcess = IntPtr.Zero;
    private IntPtr _hThread = IntPtr.Zero;
    private System.Diagnostics.Process? _unix;

    public uint ProcessId { get; private set; }

    private readonly Queue<string> _engineLines = new();

    private ManualResetEventSlim _drained = new(true);
    private int _readersLeft;

    public void Start(string exePath, string configPath, string workingDirectory)
    {
        lock (_engineLines) _engineLines.Clear();
        LastLog = null;

        // Пока читатели вывода живы, последние слова движка ещё в пути.
        _drained = new ManualResetEventSlim(false);
        _readersLeft = OperatingSystem.IsWindows() ? 1 : 2;

        if (OperatingSystem.IsWindows()) StartWindows(exePath, configPath, workingDirectory);
        else StartUnix(exePath, configPath, workingDirectory);
    }

    private void ReaderFinished()
    {
        if (Interlocked.Decrement(ref _readersLeft) <= 0) _drained.Set();
    }

    /// <summary>
    /// Ждёт, пока дочитается вывод движка. Самое важное — причина падения — приходит
    /// последней строкой, уже после выхода процесса.
    /// </summary>
    private void WaitForEngineOutput(int timeoutMs = 2000)
    {
        try { _drained.Wait(timeoutMs); } catch { }
    }

    /// <summary>Строка от движка: и в общий журнал, и в память — для разбора текущего запуска.</summary>
    private void TakeEngineLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var text = line.Trim();
        LastLog = text;

        lock (_engineLines)
        {
            if (_engineLines.Count == KeepEngineLines) _engineLines.Dequeue();
            _engineLines.Enqueue(text);
        }

        Log.Engine(text);
    }

    /// <summary>
    /// Что именно случилось с движком. Его вывод мы читаем сами на всех системах,
    /// поэтому причина берётся из строк этого запуска.
    /// </summary>
    public string Explain(string lang)
    {
        WaitForEngineOutput();

        var fromLog = EngineLogOfThisRun()
            .LastOrDefault(l => l.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
                             || l.Contains("ERROR", StringComparison.OrdinalIgnoreCase));

        var reason = fromLog ?? LastLog;
        var code = ExitCode;

        if (reason is not null)
        {
            var said = code is null or 0 ? reason : $"{reason} (код выхода {code})";
            return Hint(reason, lang) is { } hint ? $"{said} — {hint}" : said;
        }

        return code is null
            ? Strings.T(lang, "engine_died")
            : Strings.T(lang, "engine_died_code", code);
    }

    /// <summary>
    /// Движок говорит по-своему. Здесь переводим на человеческий только те беды, где человек
    /// без подсказки не поймёт, что делать: чужие адаптеры мы принципиально не убираем сами.
    /// </summary>
    public static string? Hint(string reason, string lang) =>
        reason.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            ? Strings.T(lang, "engine_tun_busy")
            : null;

    /// <summary>Строки движка с момента этого запуска.</summary>
    public IReadOnlyList<string> EngineLogOfThisRun(int maxLines = 30)
    {
        lock (_engineLines)
            return _engineLines.Count <= maxLines
                ? _engineLines.ToList()
                : _engineLines.Skip(_engineLines.Count - maxLines).ToList();
    }

    public int? ExitCode
    {
        get
        {
            try
            {
                if (_unix is not null) return _unix.HasExited ? _unix.ExitCode : null;
                if (_hProcess == IntPtr.Zero) return null;
                if (!GetExitCodeProcess(_hProcess, out var code)) return null;
                return code == 259 ? null : (int)code;
            }
            catch { return null; }
        }
    }

    private const int HANDLE_FLAG_INHERIT = 0x1;
    private const int STARTF_USESTDHANDLES = 0x00000100;

    /// <summary>
    /// Движок запускается без окна, поэтому его вывод забираем каналом: иначе на Windows
    /// причина падения не сохранялась бы нигде, а свой файл движку заводить не за чем.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void StartWindows(string exePath, string configPath, string workingDirectory)
    {
        var attrs = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        if (!CreatePipe(out var read, out var write, ref attrs, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "не удалось создать канал для вывода sing-box");

        // Наш конец канала ребёнку не отдаём, иначе после его смерти чтение не закончится.
        SetHandleInformation(read, HANDLE_FLAG_INHERIT, 0);

        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf<STARTUPINFO>();
        si.dwFlags = STARTF_USESTDHANDLES;
        si.hStdOutput = write;
        si.hStdError = write;

        var cmdLine = $"\"{exePath}\" run -c \"{configPath}\"";

        var started = CreateProcess(
            null, cmdLine, IntPtr.Zero, IntPtr.Zero, true,
            CREATE_NEW_PROCESS_GROUP | CREATE_NO_WINDOW, IntPtr.Zero, workingDirectory,
            ref si, out var pi);
        var error = Marshal.GetLastWin32Error();

        // Пишущий конец должен остаться только у движка: пока он есть у нас,
        // чтение канала не увидит конца даже после выхода движка.
        CloseHandle(write);

        if (!started)
        {
            read.Dispose();
            throw new Win32Exception(error, "не удалось запустить sing-box");
        }

        _hProcess = pi.hProcess;
        _hThread = pi.hThread;
        ProcessId = pi.dwProcessId;

        ReadPipeInBackground(read);
    }

    private void ReadPipeInBackground(SafeFileHandle read)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var stream = new FileStream(read, FileAccess.Read, 1);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } line) TakeEngineLine(line);
            }
            catch (Exception ex) { Log.Warn($"вывод движка больше не читается: {ex.Message}"); }
            finally { ReaderFinished(); }
        })
        { IsBackground = true, Name = "sing-box-log" };
        thread.Start();
    }

    private void StartUnix(string exePath, string configPath, string workingDirectory)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(configPath);

        var p = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("не удалось запустить sing-box");

        // Пустая строка от .NET означает конец потока, а не сообщение движка.
        p.OutputDataReceived += (_, e) => { if (e.Data is null) ReaderFinished(); else TakeEngineLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is null) ReaderFinished(); else TakeEngineLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        _unix = p;
        ProcessId = (uint)p.Id;
    }

    public string? LastLog { get; private set; }

    public bool IsRunning
    {
        get
        {
            if (_unix is not null) return !_unix.HasExited;
            if (_hProcess == IntPtr.Zero) return false;
            return GetExitCodeProcess(_hProcess, out var code) && code == 259;
        }
    }

    public bool Stop(int gracefulTimeoutMs = 5000)
    {
        if (_unix is not null) return StopUnix(gracefulTimeoutMs);

        if (_hProcess == IntPtr.Zero || !IsRunning) return true;

        GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, ProcessId);

        if (WaitForSingleObject(_hProcess, (uint)gracefulTimeoutMs) == 0)
            return true;

        TerminateProcess(_hProcess, 1);
        WaitForSingleObject(_hProcess, 2000);
        return false;
    }

    private bool StopUnix(int gracefulTimeoutMs)
    {
        var p = _unix!;
        if (p.HasExited) return true;

        PosixKill(p.Id, SIGTERM);
        if (p.WaitForExit(gracefulTimeoutMs)) return true;

        try { p.Kill(true); p.WaitForExit(5000); } catch { }
        return false;
    }

    public void WaitForExit()
    {
        if (_unix is not null) _unix.WaitForExit();
        else WaitForSingleObject(_hProcess, INFINITE);

        WaitForEngineOutput();
    }

    public void Dispose()
    {
        _drained.Dispose();
        _unix?.Dispose();
        _unix = null;
        if (_hThread != IntPtr.Zero) { CloseHandle(_hThread); _hThread = IntPtr.Zero; }
        if (_hProcess != IntPtr.Zero) { CloseHandle(_hProcess); _hProcess = IntPtr.Zero; }
    }
}

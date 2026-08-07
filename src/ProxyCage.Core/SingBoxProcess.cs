using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProxyCage.Core;

/// <summary>
/// Запуск и — главное — КОРРЕКТНАЯ остановка движка.
///
/// Windows: не Process.Start + Kill, потому что в TUN-режиме жёсткое убийство не даёт
/// WinTun освободить адаптер, и СЛЕДУЮЩИЙ запуск падает с
/// "configure tun interface: Cannot create a file when that file already exists".
/// Поэтому дочерний процесс создаётся в своей группе (CREATE_NEW_PROCESS_GROUP),
/// а остановка = CTRL_BREAK: Go-рантайм ловит его как os.Interrupt и закрывает TUN сам.
///
/// Unix: та же цель достигается сигналом SIGTERM. Process.Kill() в .NET шлёт SIGKILL,
/// который движок перехватить не может — а тогда на Linux остаются ip rule и таблица
/// nftables, и сеть машины ложится целиком. Поэтому kill(pid, SIGTERM) через libc.
/// </summary>
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int PosixKill(int pid, int sig);

    private IntPtr _hProcess = IntPtr.Zero;
    private IntPtr _hThread = IntPtr.Zero;
    private System.Diagnostics.Process? _unix;

    public uint ProcessId { get; private set; }

    public void Start(string exePath, string configPath, string workingDirectory)
    {
        if (OperatingSystem.IsWindows()) StartWindows(exePath, configPath, workingDirectory);
        else StartUnix(exePath, configPath, workingDirectory);
    }

    [SupportedOSPlatform("windows")]
    private void StartWindows(string exePath, string configPath, string workingDirectory)
    {
        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf<STARTUPINFO>();

        var cmdLine = $"\"{exePath}\" run -c \"{configPath}\"";

        if (!CreateProcess(
                null, cmdLine, IntPtr.Zero, IntPtr.Zero, false,
                CREATE_NEW_PROCESS_GROUP | CREATE_NO_WINDOW, IntPtr.Zero, workingDirectory,
                ref si, out var pi))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "не удалось запустить sing-box");
        }

        _hProcess = pi.hProcess;
        _hThread = pi.hThread;
        ProcessId = pi.dwProcessId;
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

        // не читать потоки нельзя: движок пишет в stderr постоянно, буфер трубы заполнится
        // и он встанет намертво уже после успешного старта
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) LastLog = e.Data; };
        p.OutputDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();

        _unix = p;
        ProcessId = (uint)p.Id;
    }

    /// <summary>Последняя строка из stderr движка — её показываем, если он не поднялся.</summary>
    public string? LastLog { get; private set; }

    public bool IsRunning
    {
        get
        {
            if (_unix is not null) return !_unix.HasExited;
            if (_hProcess == IntPtr.Zero) return false;
            return GetExitCodeProcess(_hProcess, out var code) && code == 259; // STILL_ACTIVE
        }
    }

    /// <summary>
    /// Штатная остановка. false — пришлось убивать принудительно; тогда за системой
    /// остаётся мусор, который снимет TunCleanup при следующем старте.
    /// </summary>
    public bool Stop(int gracefulTimeoutMs = 10000)
    {
        if (_unix is not null) return StopUnix(gracefulTimeoutMs);

        if (_hProcess == IntPtr.Zero || !IsRunning) return true;

        // группа == PID дочернего, т.к. он создан с CREATE_NEW_PROCESS_GROUP
        GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, ProcessId);

        if (WaitForSingleObject(_hProcess, (uint)gracefulTimeoutMs) == 0)
            return true;

        TerminateProcess(_hProcess, 1);
        WaitForSingleObject(_hProcess, 5000);
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
        if (_unix is not null) { _unix.WaitForExit(); return; }
        WaitForSingleObject(_hProcess, INFINITE);
    }

    public void Dispose()
    {
        _unix?.Dispose();
        _unix = null;
        if (_hThread != IntPtr.Zero) { CloseHandle(_hThread); _hThread = IntPtr.Zero; }
        if (_hProcess != IntPtr.Zero) { CloseHandle(_hProcess); _hProcess = IntPtr.Zero; }
    }
}

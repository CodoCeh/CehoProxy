using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ProxyCage.Core;

/// <summary>
/// Список процессов с владельцем и командной строкой без PowerShell: прямой вызов Windows
/// занимает миллисекунды, а Get-CimInstance с GetOwner на каждый процесс браузера — секунды.
/// Если вызов недоступен, возвращает null, и вызывающий берёт прежний путь через PowerShell.
/// </summary>
internal static class WindowsProcessQuery
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int ProcessCommandLineInformation = 60;
    private const int TokenUser = 1;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int information, IntPtr buffer, int length, out int returned);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int information, IntPtr buffer, int length, out int returned);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupAccountSid(string? system, IntPtr sid, StringBuilder name, ref int nameLength,
        StringBuilder domain, ref int domainLength, out int use);

    public static List<(int Pid, string User, string CommandLine)>? Query(string exeName)
    {
        try
        {
            var name = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName[..^4] : exeName;
            var result = new List<(int, string, string)>();
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch { return null; }
            foreach (var process in processes)
            {
                using (process)
                {
                    var handle = OpenProcess(QueryLimitedInformation, false, process.Id);
                    if (handle == IntPtr.Zero) continue;
                    try
                    {
                        var commandLine = CommandLine(handle);
                        if (commandLine is null) continue;
                        result.Add((process.Id, Owner(handle), commandLine));
                    }
                    finally { CloseHandle(handle); }
                }
            }
            return result.Count == 0 && processes.Length > 0 ? null : result;
        }
        catch (EntryPointNotFoundException) { return null; }
        catch (DllNotFoundException) { return null; }
    }

    private static string? CommandLine(IntPtr handle)
    {
        var size = 2048;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, size, out var needed);
                if (status == 0)
                {
                    var length = (ushort)Marshal.ReadInt16(buffer);
                    var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    return length == 0 || text == IntPtr.Zero ? "" : Marshal.PtrToStringUni(text, length / 2);
                }
                if (status != StatusInfoLengthMismatch && status != StatusBufferTooSmall) return null;
                size = Math.Max(needed, size * 2);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    private static string Owner(IntPtr handle)
    {
        if (!OpenProcessToken(handle, TokenQuery, out var token)) return "";
        try
        {
            GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return "";
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetTokenInformation(token, TokenUser, buffer, needed, out _)) return "";
                var sid = Marshal.ReadIntPtr(buffer);
                var name = new StringBuilder(256);
                var domain = new StringBuilder(256);
                int nameLength = name.Capacity, domainLength = domain.Capacity;
                return LookupAccountSid(null, sid, name, ref nameLength, domain, ref domainLength, out _) ? name.ToString() : "";
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }
}

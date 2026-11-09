using System.Runtime.InteropServices;
using System.Text;

namespace ProxyCage.Core;

/// <summary>
/// Командная строка чужого процесса напрямую из его памяти. Запуск PowerShell ради одной строки
/// занимает секунды (на Windows Server — до трёх), а такие проверки идут при каждом пуске и остановке.
/// </summary>
internal static class WindowsProcessInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int kind, ref ProcessBasicInformation info, int size, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool IsWow64Process(IntPtr process, out bool wow64);

    /// <returns>Строка запуска; null — прочитать не удалось (32-битный или чужой процесс), нужен запасной путь.</returns>
    public static string? CommandLine(int pid)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return null;
        var handle = OpenProcess(0x0410, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            if (IsWow64Process(handle, out var wow) && wow) return null;
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0) return null;

            var pointer = new byte[8];
            if (!ReadProcessMemory(handle, info.PebBaseAddress + 0x20, pointer, 8, out _)) return null;
            var parameters = (IntPtr)BitConverter.ToInt64(pointer, 0);

            var text = new byte[16];
            if (!ReadProcessMemory(handle, parameters + 0x70, text, 16, out _)) return null;
            int length = BitConverter.ToUInt16(text, 0);
            if (length == 0) return "";
            if (length > 65000) return null;
            var buffer = new byte[length];
            if (!ReadProcessMemory(handle, (IntPtr)BitConverter.ToInt64(text, 8), buffer, length, out _)) return null;
            return Encoding.Unicode.GetString(buffer);
        }
        catch { return null; }
        finally { CloseHandle(handle); }
    }
}

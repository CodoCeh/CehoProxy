using System.Runtime.InteropServices;

namespace ProxyCage.Core;

public static class DeviceRemoval
{
    private const uint DigcfAllClasses = 0x04;
    private const uint DifRemove = 0x05;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevinfoData data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr set, ref SpDevinfoData data, [Out] char[] buffer, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref SpDevinfoData data);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    public static bool TryRemove(string instanceId, out string error)
    {
        error = "";
        if (!OperatingSystem.IsWindows())
        {
            error = "не Windows";
            return false;
        }

        var set = SetupDiGetClassDevs(IntPtr.Zero, @"SWD\Wintun", IntPtr.Zero, DigcfAllClasses);
        if (set == InvalidHandle)
        {
            error = $"SetupDiGetClassDevs: {Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            var buffer = new char[512];
            for (uint i = 0; ; i++)
            {
                var data = new SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref data))
                {
                    error = "устройство не найдено";
                    return false;
                }

                if (!SetupDiGetDeviceInstanceId(set, ref data, buffer, (uint)buffer.Length, out var len)) continue;
                var id = new string(buffer, 0, (int)Math.Max(0, len - 1));
                if (!string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase)) continue;

                if (SetupDiCallClassInstaller(DifRemove, set, ref data)) return true;
                error = $"DIF_REMOVE: {Marshal.GetLastWin32Error()}";
                return false;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }
}

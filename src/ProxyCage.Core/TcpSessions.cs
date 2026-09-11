using System.Net;
using System.Runtime.InteropServices;

namespace ProxyCage.Core;

/// <summary>
/// Рвёт установленные IPv4 TCP одного процесса через <c>SetTcpEntry(DELETE_TCB)</c>.
/// Процесс не убиваем: так Telegram переподключается, а окно остаётся.
/// IPv6 не трогаем — у <c>SetTcpEntry</c> нет пары, DNS у нас ipv4_only.
/// </summary>
internal static class TcpSessions
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint MibTcpStateEstab = 5;
    private const uint MibTcpStateDeleteTcb = 12;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable, ref int size, bool sort, int ipVersion, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetTcpEntry(ref MibTcpRow row);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    public static int ResetEstablishedIPv4(int pid, string tunPrefix, Action<string>? log)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0) return 0;

        var closed = 0;
        foreach (var row in ReadEstablished(pid))
        {
            var local = ToIpv4(row.LocalAddr);
            var remote = ToIpv4(row.RemoteAddr);
            if (!IsolatedAppBounce.ShouldResetConnection(local, remote, tunPrefix)) continue;

            var close = new MibTcpRow
            {
                State = MibTcpStateDeleteTcb,
                LocalAddr = row.LocalAddr,
                LocalPort = row.LocalPort,
                RemoteAddr = row.RemoteAddr,
                RemotePort = row.RemotePort,
            };
            try
            {
                var err = SetTcpEntry(ref close);
                if (err == 0)
                {
                    closed++;
                    continue;
                }

                log?.Invoke($"не удалось сбросить TCP pid {pid} {local} → {remote}: {err}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"не удалось сбросить TCP pid {pid}: {ex.Message}");
            }
        }

        return closed;
    }

    private static List<MibTcpRowOwnerPid> ReadEstablished(int pid)
    {
        var list = new List<MibTcpRowOwnerPid>();
        if (!OperatingSystem.IsWindows()) return list;
        var size = 0;
        var code = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AfInet, TcpTableOwnerPidAll, 0);
        if (code != ErrorInsufficientBuffer || size <= 0) return list;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            code = GetExtendedTcpTable(buffer, ref size, true, AfInet, TcpTableOwnerPidAll, 0);
            if (code != 0) return list;

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var first = IntPtr.Add(buffer, sizeof(int));
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(first, i * rowSize));
                if (row.OwningPid == (uint)pid && row.State == MibTcpStateEstab)
                    list.Add(row);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return list;
    }

    private static string ToIpv4(uint addr) => new IPAddress(BitConverter.GetBytes(addr)).ToString();
}

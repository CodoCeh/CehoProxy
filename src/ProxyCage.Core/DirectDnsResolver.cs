using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ProxyCage.Core;

/// <summary>
/// Резолв имён напрямую через публичные DNS, минуя системный резолвер.
/// Нужен, когда TUN перехватывает DNS Windows и ломает скачивание подписок.
/// </summary>
public static class DirectDnsResolver
{
    private static readonly string[] Resolvers = { Os.PublicResolver, "8.8.8.8", "9.9.9.9" };

    public static async Task<IPAddress[]> ResolveAsync(
        string host, CancellationToken cancellationToken = default, string? tunAddress = null)
    {
        if (IPAddress.TryParse(host, out var literal))
            return new[] { literal };

        var normalized = host.Trim().TrimEnd('.');
        if (normalized.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        foreach (var server in Resolvers)
        {
            try
            {
                var addresses = await QueryAsync(server, normalized, cancellationToken, tunAddress);
                if (addresses.Length > 0) return addresses;
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        throw new SocketException((int)SocketError.HostNotFound);
    }

    private static async Task<IPAddress[]> QueryAsync(
        string server, string host, CancellationToken cancellationToken, string? tunAddress)
    {
        var query = BuildQuery(host, (ushort)Random.Shared.Next(ushort.MaxValue));
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = 4000;
        var bind = Os.PhysicalBindAddress(tunAddress);
        if (bind is not null)
            udp.Client.Bind(new IPEndPoint(bind, 0));

        var endpoint = new IPEndPoint(IPAddress.Parse(server), 53);
        await udp.SendAsync(query, query.Length, endpoint);

        using var timeout = cancellationToken.Register(() => udp.Close());
        var result = await udp.ReceiveAsync(cancellationToken);
        return ParseARecords(result.Buffer, host);
    }

    private static byte[] BuildQuery(string host, ushort id)
    {
        using var ms = new MemoryStream(64);
        ms.WriteByte((byte)(id >> 8));
        ms.WriteByte((byte)(id & 0xff));
        ms.WriteByte(0x01); // RD
        ms.WriteByte(0x00);
        WriteUInt16(ms, 1); // questions
        WriteUInt16(ms, 0);
        WriteUInt16(ms, 0);
        WriteUInt16(ms, 0);

        foreach (var label in host.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (label.Length is 0 or > 63)
                throw new FormatException($"bad dns label: {host}");
            ms.WriteByte((byte)label.Length);
            ms.Write(Encoding.ASCII.GetBytes(label));
        }
        ms.WriteByte(0);
        WriteUInt16(ms, 1); // A
        WriteUInt16(ms, 1); // IN
        return ms.ToArray();
    }

    private static IPAddress[] ParseARecords(byte[] packet, string host)
    {
        if (packet.Length < 12) return Array.Empty<IPAddress>();

        var flags = (packet[2] << 8) | packet[3];
        if ((flags & 0x8000) == 0) return Array.Empty<IPAddress>();
        if ((flags & 0x000F) != 0) return Array.Empty<IPAddress>();

        var questions = (packet[4] << 8) | packet[5];
        var answers = (packet[6] << 8) | packet[7];
        var offset = 12;

        for (var q = 0; q < questions; q++)
        {
            offset = SkipName(packet, offset);
            offset += 4;
            if (offset > packet.Length) return Array.Empty<IPAddress>();
        }

        var found = new List<IPAddress>();
        for (var a = 0; a < answers; a++)
        {
            offset = ReadRecord(packet, offset, found);
            if (offset < 0) break;
        }

        return found.Count > 0
            ? found.ToArray()
            : Array.Empty<IPAddress>();
    }

    private static int ReadRecord(byte[] packet, int offset, List<IPAddress> found)
    {
        offset = SkipName(packet, offset);
        if (offset + 10 > packet.Length) return -1;

        var type = (packet[offset] << 8) | packet[offset + 1];
        var rdLength = (packet[offset + 8] << 8) | packet[offset + 9];
        offset += 10;

        if (offset + rdLength > packet.Length) return -1;

        if (type == 1 && rdLength == 4)
        {
            found.Add(new IPAddress(new[]
            {
                packet[offset], packet[offset + 1], packet[offset + 2], packet[offset + 3],
            }));
        }

        return offset + rdLength;
    }

    private static int SkipName(byte[] packet, int offset)
    {
        while (offset < packet.Length)
        {
            var len = packet[offset];
            if (len == 0) return offset + 1;
            if ((len & 0xC0) == 0xC0) return offset + 2;
            offset += 1 + len;
        }
        return offset;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xff));
    }
}

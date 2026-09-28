using ProxyCage.Core;
using Tmds.DBus.Protocol;

namespace ProxyCage.Tray.Linux;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine(Updater.CurrentVersion);
            return 0;
        }

        var address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address))
        {
            Console.Error.WriteLine(Strings.T(null, "tray_no_desktop"));
            return 1;
        }

        using var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        var patience = args.Contains("--restart") ? 20 : 0;
        while (!await connection.TryRequestNameAsync(TrayItem.SingleName, RequestNameOptions.None))
        {
            if (patience-- <= 0) return 0;
            await Task.Delay(1000);
        }

        var root = Environment.GetEnvironmentVariable("CEHOPROXY_HOME") ?? Os.DefaultRoot;
        using var tray = new TrayItem(connection, root);
        await tray.RunAsync();
        return 0;
    }
}

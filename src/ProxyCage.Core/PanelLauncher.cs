using System.Diagnostics;
using System.Security.Cryptography;

namespace ProxyCage.Core;

/// <summary>Что делает ярлык, пункт меню «Панель CehoProxy» и двойной щелчок по значку: открывает веб-панель, окно Light или спрашивает.</summary>
public static class PanelLauncher
{
    public static string ResolveTarget(string? requested, string? stored)
    {
        foreach (var value in new[] { requested, stored })
            if (value is not null && CehoConfig.NormalizeOpenTarget(value) is var t
                && (t != CehoConfig.OpenAsk || string.Equals(value, CehoConfig.OpenAsk, StringComparison.OrdinalIgnoreCase)))
                return t;
        return CehoConfig.OpenAsk;
    }

    /// <returns>0 — открыли (или человек закрыл окно выбора), 1 — служба не оставила адрес панели.</returns>
    public static async Task<int> OpenAsync(string root, string? requested = null, CancellationToken cancellationToken = default)
    {
        if (Auth.ReadPanelPointer(root) is not { } port) return 1;
        var url = $"http://127.0.0.1:{port}";
        var target = ResolveTarget(requested, Auth.ReadOpenTarget(root));
        if (target == CehoConfig.OpenAsk) return await ChooseAsync(url, cancellationToken);
        Launch(target, url, Auth.ReadLightPointer(root) is not null);
        return 0;
    }

    private static void Launch(string target, string url, bool lightAvailable)
    {
        if (target == CehoConfig.OpenLight && lightAvailable) LightWindow.Open(url + "/light");
        else Os.OpenInBrowser(url);
    }

    private static async Task<int> ChooseAsync(string url, CancellationToken cancellationToken)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var page = $"{url}/open?n={nonce}";
        var window = LightWindow.OpenChooser(page);
        if (window is null) Os.OpenInBrowser(page);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var until = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < until && !cancellationToken.IsCancellationRequested)
        {
            string answer = "";
            try { answer = (await http.GetStringAsync($"{url}/open-pick?n={nonce}", cancellationToken)).Trim(); }
            catch { }
            if (answer is CehoConfig.OpenWeb or CehoConfig.OpenLight)
            {
                Close(window);
                Launch(answer, url, lightAvailable: true);
                return 0;
            }
            if (window is not null && Exited(window)) return 0;
            try { await Task.Delay(300, cancellationToken); } catch (OperationCanceledException) { break; }
        }
        Close(window);
        return 0;
    }

    private static bool Exited(Process process)
    {
        try { process.Refresh(); return process.HasExited; }
        catch { return true; }
    }

    private static void Close(Process? process)
    {
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        process.Dispose();
    }
}

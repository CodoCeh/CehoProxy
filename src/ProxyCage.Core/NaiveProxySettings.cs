namespace ProxyCage.Core;

/// <summary>
/// Standalone NaiveProxy / Caddy upstream (HTTPS :8443 with basic auth).
/// </summary>
public sealed class NaiveProxySettings
{
    public bool Enabled { get; set; }

    public string Server { get; set; } = "";

    public int Port { get; set; } = 8443;

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>TLS SNI / server_name. Empty — same as <see cref="Server"/>.</summary>
    public string? ServerName { get; set; }

    public string Remark { get; set; } = "NaiveProxy";

    public bool AllowInsecure { get; set; }

    /// <summary>Expected public IP via this proxy (for connection test). Empty — any IP ok.</summary>
    public string? ExpectedExitIp { get; set; }

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Server)
        && Port is > 0 and <= 65535
        && !string.IsNullOrWhiteSpace(Username)
        && Password.Length > 0;
}

public static class NaiveProxyHelper
{
    public const string DefaultTag = "naive-bsv";

    public static ProxyNode ToNode(NaiveProxySettings s) => new()
    {
        Tag = DefaultTag,
        Protocol = ProxyProtocol.Naive,
        Server = s.Server.Trim(),
        Port = s.Port,
        Credential = s.Username.Trim(),
        TuicPassword = s.Password,
        Sni = string.IsNullOrWhiteSpace(s.ServerName) ? s.Server.Trim() : s.ServerName.Trim(),
        Security = "tls",
        AllowInsecure = s.AllowInsecure,
        Remark = string.IsNullOrWhiteSpace(s.Remark) ? "NaiveProxy" : s.Remark.Trim(),
        CountryCode = "XX",
        CountryName = s.Remark.Trim(),
    };

    /// <summary>naive://user:pass@host:8443?sni=site.example.com#remark</summary>
    public static bool TryParseUri(string uri, out NaiveProxySettings? settings)
    {
        settings = null;
        const string prefix = "naive://";
        if (!uri.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var rest = uri.Trim()[prefix.Length..];
            var hash = rest.IndexOf('#');
            var fragment = hash >= 0 ? Uri.UnescapeDataString(rest[(hash + 1)..]) : "";
            if (hash >= 0) rest = rest[..hash];

            var qIdx = rest.IndexOf('?');
            var query = qIdx >= 0 ? rest[(qIdx + 1)..] : "";
            if (qIdx >= 0) rest = rest[..qIdx];

            var at = rest.LastIndexOf('@');
            if (at < 0) return false;
            var cred = rest[..at];
            var hostPort = rest[(at + 1)..];
            var colon = hostPort.LastIndexOf(':');
            if (colon < 0 || !int.TryParse(hostPort[(colon + 1)..], out var port)) return false;

            var userColon = cred.IndexOf(':');
            if (userColon < 0) return false;

            var q = System.Web.HttpUtility.ParseQueryString(query);
            settings = new NaiveProxySettings
            {
                Enabled = true,
                Server = hostPort[..colon],
                Port = port,
                Username = Uri.UnescapeDataString(cred[..userColon]),
                Password = Uri.UnescapeDataString(cred[(userColon + 1)..]),
                ServerName = q["sni"],
                AllowInsecure = q["insecure"] == "1",
                Remark = fragment.Length > 0 ? fragment : "NaiveProxy",
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>naive://user:pass@host:8443?sni=site.example.com#remark</summary>
    public static string BuildUri(NaiveProxySettings s)
    {
        var user = Uri.EscapeDataString(s.Username.Trim());
        var pass = Uri.EscapeDataString(s.Password);
        var host = s.Server.Trim();
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.ServerName))
            query.Add($"sni={Uri.EscapeDataString(s.ServerName.Trim())}");
        if (s.AllowInsecure)
            query.Add("insecure=1");
        var q = query.Count > 0 ? "?" + string.Join("&", query) : "";
        var remark = string.IsNullOrWhiteSpace(s.Remark) ? "NaiveProxy" : s.Remark.Trim();
        return $"naive://{user}:{pass}@{host}:{s.Port}{q}#{Uri.EscapeDataString(remark)}";
    }
}

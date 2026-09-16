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

    /// <summary>Checks whether the string represents a NaiveProxy node URI.</summary>
    public static bool IsNaiveUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var trimmed = uri.Trim();
        if (trimmed.StartsWith("naive://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("naive+quic://", StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("http2://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("quic://", StringComparison.OrdinalIgnoreCase))
        {
            var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0) return false;
            var afterScheme = trimmed[(schemeEnd + 3)..];
            var slash = afterScheme.IndexOf('/');
            var question = afterScheme.IndexOf('?');
            var hash = afterScheme.IndexOf('#');
            var endOfAuthority = afterScheme.Length;
            if (slash >= 0 && slash < endOfAuthority) endOfAuthority = slash;
            if (question >= 0 && question < endOfAuthority) endOfAuthority = question;
            if (hash >= 0 && hash < endOfAuthority) endOfAuthority = hash;

            var authority = afterScheme[..endOfAuthority];
            return authority.Contains('@');
        }

        return false;
    }

    /// <summary>
    /// Parses NaiveProxy / Caddy upstream URIs in various formats:
    /// - naive://user:pass@host:8443?sni=site.com#remark
    /// - naive+https://user:pass@host:8443...
    /// - https://user:pass@host:8443/
    /// - quic://user:pass@host:8443...
    /// </summary>
    public static bool TryParseUri(string uri, out NaiveProxySettings? settings)
    {
        settings = null;
        if (!IsNaiveUri(uri)) return false;

        try
        {
            var trimmed = uri.Trim();
            string defaultScheme = "https";
            string rest;

            if (trimmed.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase))
            {
                defaultScheme = "https";
                rest = trimmed["naive+https://".Length..];
            }
            else if (trimmed.StartsWith("naive+quic://", StringComparison.OrdinalIgnoreCase))
            {
                defaultScheme = "quic";
                rest = trimmed["naive+quic://".Length..];
            }
            else if (trimmed.StartsWith("naive://", StringComparison.OrdinalIgnoreCase))
            {
                defaultScheme = "https";
                rest = trimmed["naive://".Length..];
            }
            else
            {
                var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
                if (schemeEnd < 0) return false;
                defaultScheme = trimmed[..schemeEnd].ToLowerInvariant();
                rest = trimmed[(schemeEnd + 3)..];
            }

            var hash = rest.IndexOf('#');
            var fragment = hash >= 0 ? Uri.UnescapeDataString(rest[(hash + 1)..]) : "";
            if (hash >= 0) rest = rest[..hash];

            var qIdx = rest.IndexOf('?');
            var query = qIdx >= 0 ? rest[(qIdx + 1)..] : "";
            if (qIdx >= 0) rest = rest[..qIdx];

            var slash = rest.IndexOf('/');
            if (slash >= 0) rest = rest[..slash];

            var at = rest.LastIndexOf('@');
            if (at < 0) return false;
            var cred = rest[..at];
            var hostPort = rest[(at + 1)..];

            string username;
            string password;
            var userColon = cred.IndexOf(':');
            if (userColon >= 0)
            {
                username = Uri.UnescapeDataString(cred[..userColon]);
                password = Uri.UnescapeDataString(cred[(userColon + 1)..]);
            }
            else
            {
                username = Uri.UnescapeDataString(cred);
                password = "";
            }

            string host;
            int port;

            if (hostPort.StartsWith("[", StringComparison.Ordinal))
            {
                var closeBracket = hostPort.IndexOf(']');
                if (closeBracket < 0) return false;
                host = hostPort[1..closeBracket];
                var afterBracket = hostPort[(closeBracket + 1)..];
                if (afterBracket.StartsWith(":", StringComparison.Ordinal))
                {
                    if (!int.TryParse(afterBracket[1..], out port) || port is <= 0 or > 65535)
                        return false;
                }
                else
                {
                    port = defaultScheme == "http" ? 80 : 443;
                }
            }
            else
            {
                var colon = hostPort.LastIndexOf(':');
                if (colon >= 0)
                {
                    host = hostPort[..colon];
                    if (!int.TryParse(hostPort[(colon + 1)..], out port) || port is <= 0 or > 65535)
                        return false;
                }
                else
                {
                    host = hostPort;
                    port = defaultScheme == "http" ? 80 : 443;
                }
            }

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
                return false;

            var q = System.Web.HttpUtility.ParseQueryString(query);
            var sni = q["sni"];
            if (string.IsNullOrWhiteSpace(sni))
            {
                if (!System.Net.IPAddress.TryParse(host, out _))
                    sni = host;
            }

            var remark = fragment.Length > 0
                ? fragment
                : (defaultScheme == "quic" ? "NaiveQuic" : "NaiveProxy");

            settings = new NaiveProxySettings
            {
                Enabled = true,
                Server = host,
                Port = port,
                Username = username,
                Password = password,
                ServerName = sni,
                AllowInsecure = q["insecure"] == "1",
                Remark = remark,
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

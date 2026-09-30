using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxyCage.Core;

public static class SettingsTransfer
{
    public const string Header = "CehoProxy settings v1";
    public const string FileName = "cehoproxy-settings.chps";
    private const int Iterations = 200_000;

    [Flags]
    public enum Parts { None = 0, Vpn = 1, Apps = 2, Sites = 4, Other = 8, All = 15 }

    public sealed record ImportResult(
        Parts Applied, int SubFiles, IReadOnlyList<string> AppsAdded,
        IReadOnlyList<string> AppsHad, IReadOnlyList<string> AppsMissing);

    public sealed class WrongPasswordException : Exception
    {
        public WrongPasswordException() : base("wrong password or damaged file") { }
    }

    private static readonly string[] VpnKeys =
    {
        nameof(CehoConfig.Subscriptions), nameof(CehoConfig.ActiveSubscription), nameof(CehoConfig.NaiveProxy),
        nameof(CehoConfig.PreferredCountries), nameof(CehoConfig.ExcludedCountries), nameof(CehoConfig.BlockedNodes),
        nameof(CehoConfig.RotationEnabled), nameof(CehoConfig.MaxLatencyMs),
    };

    private static readonly string[] AppKeys = { nameof(CehoConfig.Apps) };

    private static readonly string[] SiteKeys =
        { nameof(CehoConfig.DirectSites), nameof(CehoConfig.SiteCountries), nameof(CehoConfig.SiteMode) };

    private static readonly string[] LocalKeys = { nameof(CehoConfig.WebPort), nameof(CehoConfig.MixedPort) };

    private static bool InOther(string key) =>
        !VpnKeys.Contains(key) && !AppKeys.Contains(key) && !SiteKeys.Contains(key) && !LocalKeys.Contains(key);

    private static bool Wanted(Parts parts, string key) =>
        (parts.HasFlag(Parts.Vpn) && VpnKeys.Contains(key)) ||
        (parts.HasFlag(Parts.Apps) && AppKeys.Contains(key)) ||
        (parts.HasFlag(Parts.Sites) && SiteKeys.Contains(key)) ||
        (parts.HasFlag(Parts.Other) && InOther(key));

    private static readonly (Parts Part, string Name, string Ru)[] PartNames =
    {
        (Parts.Vpn, "vpn", "vpn"), (Parts.Apps, "apps", "программы"),
        (Parts.Sites, "sites", "сайты"), (Parts.Other, "other", "прочее"),
    };

    public static Parts ParseParts(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Parts.All;
        var result = Parts.None;
        foreach (var raw in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var word = raw.ToLowerInvariant();
            if (word is "all" or "все" or "всё") return Parts.All;
            foreach (var (part, name, ru) in PartNames)
                if (word == name || word == ru || word == ((int)Math.Log2((int)part) + 1).ToString())
                    result |= part;
        }
        return result;
    }

    public static string PartList(Parts parts) =>
        string.Join(",", PartNames.Where(n => parts.HasFlag(n.Part)).Select(n => n.Name));


    public static string Export(string root, string password, Parts parts = Parts.All)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("password");
        if (parts == Parts.None) throw new ArgumentException("parts");
        var subs = new JsonObject();
        if (parts.HasFlag(Parts.Vpn))
            foreach (var file in Directory.EnumerateFiles(root, "sub-*.txt"))
                subs[Path.GetFileName(file)] = File.ReadAllText(file);
        var full = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "config.json")))!.AsObject();
        var kept = new JsonObject();
        foreach (var (key, value) in full)
            if (Wanted(parts, key)) kept[key] = value?.DeepClone();
        var payload = new JsonObject
        {
            ["parts"] = PartList(parts),
            ["config"] = kept.ToJsonString(),
            ["subs"] = subs,
        }.ToJsonString();
        return Header + "\n" + Convert.ToBase64String(Seal(Encoding.UTF8.GetBytes(payload), password)) + "\n";
    }

    public static ImportResult Import(string root, string text, string password,
        Parts parts = Parts.All, Func<AppEntry, AppEntry?>? resolveApp = null)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2 || lines[0].Trim() != Header) throw new InvalidDataException("not a CehoProxy settings file");
        byte[] sealedBytes;
        try { sealedBytes = Convert.FromBase64String(string.Concat(lines.Skip(1)).Trim()); }
        catch (FormatException) { throw new InvalidDataException("damaged settings file"); }

        var payload = JsonNode.Parse(Encoding.UTF8.GetString(Open(sealedBytes, password)))!.AsObject();
        var imported = JsonNode.Parse(payload["config"]!.GetValue<string>())!.AsObject();
        var inFile = payload["parts"] is { } listed ? ParseParts(listed.GetValue<string>()) : Parts.All;
        var applied = parts & inFile;

        var configPath = Path.Combine(root, "config.json");
        var local = File.Exists(configPath)
            ? JsonNode.Parse(File.ReadAllText(configPath))!.AsObject()
            : new JsonObject();
        var merged = JsonNode.Parse(local.ToJsonString())!.AsObject();
        foreach (var (key, value) in imported)
            if (Wanted(applied, key) && key != nameof(CehoConfig.Apps))
                merged[key] = value?.DeepClone();

        var probe = Path.Combine(root, "config.import.json");
        File.WriteAllText(probe, merged.ToJsonString());
        var added = new List<string>();
        var had = new List<string>();
        var missing = new List<string>();
        try
        {
            var cfg = CehoConfig.Load(probe);
            if (applied.HasFlag(Parts.Apps) && imported[nameof(CehoConfig.Apps)] is JsonArray apps)
            {
                var resolve = resolveApp ?? (a => ResolveOnThisMachine(a, cfg.Language));
                foreach (var node in apps)
                {
                    var app = node.Deserialize<AppEntry>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (app is null) continue;
                    var found = resolve(app);
                    if (found is null) { missing.Add(app.Label); continue; }
                    if (cfg.Apps.Any(x => x.Folder.Equals(found.Folder, StringComparison.OrdinalIgnoreCase)))
                    { had.Add(app.Label); continue; }
                    cfg.Apps.Add(found);
                    added.Add(app.Label);
                }
            }
            cfg.Save(configPath);
        }
        finally { try { File.Delete(probe); } catch { } }

        var count = 0;
        if (applied.HasFlag(Parts.Vpn) && payload["subs"] is JsonObject subs)
            foreach (var (name, value) in subs)
            {
                if (value is null || !IsSubFileName(name)) continue;
                File.WriteAllText(Path.Combine(root, name), value.GetValue<string>());
                count++;
            }
        Auth.RestrictConfigAccess(configPath);
        return new ImportResult(applied, count, added, had, missing);
    }

    public static AppEntry? ResolveOnThisMachine(AppEntry app, string lang)
    {
        if (app.Folder.Length > 0 && (Directory.Exists(app.Folder) || File.Exists(app.Folder))) return app;

        var names = new[] { app.Name, app.Label }.Where(n => n.Length > 0).ToArray();
        bool Same(string name) => names.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

        string? path = null;
        try { path = InstalledAppCatalog.Detect(lang).FirstOrDefault(e => Same(e.Name))?.Path; } catch { }
        if (path is null)
            try { path = AiTools.Detect().FirstOrDefault(t => Same(t.Name))?.Path; } catch { }
        if (path is null) return null;

        try
        {
            var d = AppDetector.Detect(path, lang);
            return new AppEntry
            {
                Name = d.Name, DisplayName = app.DisplayName, Folder = d.Folder,
                VersionAgnostic = d.VersionAgnostic, SingleFile = d.SingleFile,
                Launch = File.Exists(path) ? path : null,
                Enabled = app.Enabled, NoInternet = app.NoInternet, AllowedNodes = app.AllowedNodes,
            };
        }
        catch { return null; }
    }

    public static string Describe(ImportResult r, string lang)
    {
        var names = string.Join(", ", PartNames.Where(n => r.Applied.HasFlag(n.Part))
            .Select(n => Strings.T(lang, "transfer_part_" + n.Name)));
        var text = new StringBuilder(Strings.T(lang, "transfer_done_head", names.Length > 0 ? names : "-"));
        if (r.Applied.HasFlag(Parts.Apps))
        {
            text.Append(' ').Append(Strings.T(lang, "transfer_apps_result", r.AppsAdded.Count, r.AppsHad.Count, r.AppsMissing.Count));
            if (r.AppsMissing.Count > 0)
                text.Append(' ').Append(Strings.T(lang, "transfer_apps_missing", string.Join(", ", r.AppsMissing)));
        }
        if (r.Applied.HasFlag(Parts.Vpn))
            text.Append(' ').Append(Strings.T(lang, "transfer_subs_files", r.SubFiles));
        return text.ToString();
    }

    internal static bool IsSubFileName(string name) =>
        name.StartsWith("sub-", StringComparison.Ordinal) && name.EndsWith(".txt", StringComparison.Ordinal)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains("..", StringComparison.Ordinal)
        && Path.GetFileName(name) == name;

    private static (byte[] Enc, byte[] Mac) Keys(string password, byte[] salt)
    {
        var both = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 64);
        return (both[..32], both[32..]);
    }

    internal static byte[] Seal(byte[] plain, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var (enc, macKey) = Keys(password, salt);
        using var aes = Aes.Create();
        aes.Key = enc;
        aes.GenerateIV();
        var cipher = aes.EncryptCbc(plain, aes.IV);
        var body = salt.Concat(aes.IV).Concat(cipher).ToArray();
        return body.Concat(HMACSHA256.HashData(macKey, body)).ToArray();
    }

    internal static byte[] Open(byte[] sealedBytes, string password)
    {
        if (sealedBytes.Length < 16 + 16 + 16 + 32) throw new WrongPasswordException();
        var body = sealedBytes[..^32];
        var mac = sealedBytes[^32..];
        var salt = body[..16];
        var (enc, macKey) = Keys(password, salt);
        if (!CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(macKey, body)))
            throw new WrongPasswordException();
        using var aes = Aes.Create();
        aes.Key = enc;
        return aes.DecryptCbc(body[32..], body[16..32]);
    }
}

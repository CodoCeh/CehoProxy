using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxyCage.Core;

/// <summary>
/// A private, local checkpoint of rules that actually ran successfully. Separate from
/// encrypted settings export and binary updater rollback. Saving settings never advances it.
/// </summary>
public static class VerifiedConfigStore
{
    public const string FileName = "config.verified.json";
    private const int MaxBytes = 32 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] RoutingKeys =
    {
        nameof(CehoConfig.MixedPort), nameof(CehoConfig.ClashApiPort), nameof(CehoConfig.TunAddress),
        nameof(CehoConfig.TunIpv6), nameof(CehoConfig.FailClosed), nameof(CehoConfig.Apps),
        nameof(CehoConfig.Subscriptions), nameof(CehoConfig.NaiveProxy), nameof(CehoConfig.ActiveSubscription),
        nameof(CehoConfig.PreferredCountries), nameof(CehoConfig.ExcludedCountries), nameof(CehoConfig.DirectSites),
        nameof(CehoConfig.SiteCountries), nameof(CehoConfig.SiteMode), nameof(CehoConfig.BlockedNodes),
        nameof(CehoConfig.RotationEnabled), nameof(CehoConfig.MaxLatencyMs), nameof(CehoConfig.NodeLatency),
        nameof(CehoConfig.CheckUrl), nameof(CehoConfig.TimeoutSeconds), nameof(CehoConfig.EngineLogLevel),
    };

    public sealed class Candidate
    {
        internal string Payload { get; }
        internal Candidate(string payload) => Payload = payload;
    }

    public sealed record Status(bool Available, DateTime? VerifiedUtc, int Apps, int Subscriptions,
        bool RequiresSecurityAcknowledgement, string? Error = null);

    public sealed record RestoreResult(DateTime VerifiedUtc, int Apps, int Subscriptions);

    private sealed class Snapshot
    {
        public int Version { get; set; } = 2;
        public DateTime VerifiedUtc { get; set; }
        public string Config { get; set; } = "";
        public string ConfigHash { get; set; } = "";
        public string Runtime { get; set; } = "";
        public string RuntimeHash { get; set; } = "";
        public Dictionary<string, string> Caches { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> CacheHashes { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>Capture the exact input before starting it; not a verification operation.</summary>
    public static Candidate Capture(string root, CehoConfig cfg, string generatedJson)
    {
        cfg.Validate();
        ValidateRuntime(generatedJson);
        var all = JsonSerializer.SerializeToNode(cfg)!.AsObject();
        var routing = new JsonObject();
        foreach (var key in RoutingKeys) routing[key] = all[key]?.DeepClone();
        var config = routing.ToJsonString();
        long totalBytes = (long)Encoding.UTF8.GetByteCount(config) + Encoding.UTF8.GetByteCount(generatedJson);
        if (totalBytes > MaxBytes) throw new InvalidDataException("Verified configuration is too large.");
        var snapshot = new Snapshot { Config = config, ConfigHash = Hash(config), Runtime = generatedJson,
            RuntimeHash = Hash(generatedJson) };
        foreach (var subscription in cfg.Subscriptions)
        {
            var name = "sub-" + subscription.Name + ".txt";
            var file = Path.Combine(root, name);
            if (File.Exists(file))
            {
                snapshot.Caches[name] = ReadBounded(file);
                totalBytes += Encoding.UTF8.GetByteCount(snapshot.Caches[name]);
                if (totalBytes > MaxBytes) throw new InvalidDataException("Verified configuration is too large.");
                snapshot.CacheHashes[name] = Hash(snapshot.Caches[name]);
            }
        }
        var payload = JsonSerializer.Serialize(snapshot, Json);
        if (Encoding.UTF8.GetByteCount(payload) > MaxBytes)
            throw new InvalidDataException("Verified configuration is too large.");
        return new Candidate(payload);
    }

    /// <summary>Caller must have verified this exact candidate with a successfully running engine.</summary>
    public static void CommitVerified(string root, Candidate candidate)
        => CommitVerified(root, candidate, PrivateFile.Write);

    internal static void CommitVerified(string root, Candidate candidate, Action<string, string> write)
    {
        var snapshot = Parse(candidate.Payload);
        lock (Gate)
        {
            snapshot.VerifiedUtc = DateTime.UtcNow;
            // Keep the review token fresh even when two successful applies share a clock tick.
            try
            {
                var previous = Read(root).VerifiedUtc;
                if (snapshot.VerifiedUtc <= previous) snapshot.VerifiedUtc = previous.AddTicks(1);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or ArgumentException) { }
            var text = JsonSerializer.Serialize(snapshot, Json);
            EnsureSize(text);
            write(Path.Combine(root, FileName), text);
        }
    }

    public static Status ReadStatus(string root)
    {
        lock (Gate)
        {
            if (!File.Exists(Path.Combine(root, FileName))) return new(false, null, 0, 0, false);
            try
            {
                var snapshot = Read(root);
                var cfg = ParseConfig(snapshot);
                return new(true, snapshot.VerifiedUtc, cfg.Apps.Count, cfg.Subscriptions.Count,
                    NeedsSecurityAcknowledgement(root, snapshot));
            }
            catch { return new(false, null, 0, 0, false, "The verified checkpoint cannot be read safely."); }
        }
    }

    /// <summary>
    /// Explicit restore only. Keeps current local authentication, language, panel and updater
    /// preferences. The caller must apply/restart after staging and report that outcome separately.
    /// Never deletes or advances the verified checkpoint.
    /// </summary>
    public static RestoreResult Restore(string root, bool acknowledgeSecurityChange = false, DateTime? expectedVerifiedUtc = null)
        => Restore(root, acknowledgeSecurityChange, expectedVerifiedUtc, PrivateFile.Write);

    internal static RestoreResult Restore(string root, bool acknowledgeSecurityChange, DateTime? expectedVerifiedUtc,
        Action<string, string> write)
    {
        lock (Gate)
        {
            var snapshot = Read(root);
            if (expectedVerifiedUtc is { } expected && snapshot.VerifiedUtc != expected)
                throw new InvalidOperationException("The checkpoint changed. Review the current checkpoint before restoring.");
            if (NeedsSecurityAcknowledgement(root, snapshot) && !acknowledgeSecurityChange)
                throw new InvalidOperationException("Review and acknowledge the checkpoint's weaker TLS or leak protection before restoring.");
            var path = Path.Combine(root, "config.json");
            var current = File.Exists(path) ? JsonNode.Parse(ReadBounded(path))!.AsObject() : new JsonObject();
            var stored = JsonNode.Parse(snapshot.Config)!.AsObject();
            foreach (var key in RoutingKeys) current[key] = stored[key]?.DeepClone();
            (current.Deserialize<CehoConfig>() ?? throw new InvalidDataException("Invalid merged settings.")).Validate();
            var files = snapshot.Caches.Select(p => (Path: Path.Combine(root, p.Key), Text: p.Value)).ToList();
            files.Add((Path.Combine(root, "singbox.json"), snapshot.Runtime));
            files.Add((path, current.ToJsonString(Json)));
            foreach (var file in files)
            {
                EnsureSize(file.Text);
                PrivateFile.RejectLink(file.Path);
                if (Directory.Exists(file.Path)) throw new IOException("A configuration target is a directory.");
            }
            // Read every backup before touching any target. If one write fails, unwind the rest.
            var before = files.ToDictionary(p => p.Path, p => (Text: File.Exists(p.Path) ? ReadBounded(p.Path) : null,
                WrittenUtc: File.Exists(p.Path) ? File.GetLastWriteTimeUtc(p.Path) : (DateTime?)null));
            var changed = new List<string>();
            try
            {
                foreach (var file in files)
                {
                    // Include the attempted target: a failure can occur after its rename.
                    changed.Add(file.Path);
                    write(file.Path, file.Text);
                }
                // Doctor compares timestamps. These restored rules match the restored configuration.
                File.SetLastWriteTimeUtc(Path.Combine(root, "singbox.json"), File.GetLastWriteTimeUtc(path));
            }
            catch (Exception failure)
            {
                var undone = true;
                foreach (var file in changed.AsEnumerable().Reverse())
                    try
                    {
                        if (before[file].Text is { } text)
                        {
                            PrivateFile.Write(file, text);
                            File.SetLastWriteTimeUtc(file, before[file].WrittenUtc!.Value);
                        }
                        else File.Delete(file);
                    }
                    catch { undone = false; }
                throw new IOException(undone
                    ? "The checkpoint could not be restored; previous settings were retained."
                    : "The checkpoint could not be fully restored. The verified checkpoint is still available; retry recovery before starting protection.", failure);
            }
            var cfg = ParseConfig(snapshot);
            return new(snapshot.VerifiedUtc, cfg.Apps.Count, cfg.Subscriptions.Count);
        }
    }

    private static Snapshot Read(string root)
    {
        var snapshot = Parse(ReadBounded(Path.Combine(root, FileName)));
        if (snapshot.VerifiedUtc == default) throw new InvalidDataException("Unverified checkpoint.");
        return snapshot;
    }

    private static Snapshot Parse(string text)
    {
        var snapshot = JsonSerializer.Deserialize<Snapshot>(text) ?? throw new InvalidDataException("Empty checkpoint.");
        if (snapshot.Version != 2 || snapshot.Caches is null || snapshot.CacheHashes is null
            || snapshot.Config is null || snapshot.Runtime is null
            || snapshot.Caches.Any(p => !IsCacheName(p.Key) || p.Value is null))
            throw new InvalidDataException("Unsupported checkpoint.");
        if (snapshot.ConfigHash != Hash(snapshot.Config)) throw new InvalidDataException("Damaged checkpoint settings.");
        if (snapshot.RuntimeHash != Hash(snapshot.Runtime)) throw new InvalidDataException("Damaged checkpoint.");
        ValidateRuntime(snapshot.Runtime);
        var cfg = ParseConfig(snapshot);
        var names = cfg.Subscriptions.Select(s => "sub-" + s.Name + ".txt").ToHashSet(StringComparer.Ordinal);
        if (snapshot.Caches.Count != snapshot.CacheHashes.Count
            || snapshot.Caches.Any(p => !names.Contains(p.Key) || !snapshot.CacheHashes.TryGetValue(p.Key, out var hash) || hash != Hash(p.Value)))
            throw new InvalidDataException("Damaged checkpoint subscription cache.");
        return snapshot;
    }

    private static CehoConfig ParseConfig(Snapshot snapshot)
    {
        var obj = JsonNode.Parse(snapshot.Config)?.AsObject() ?? throw new InvalidDataException("Invalid settings.");
        if (obj.Any(p => !RoutingKeys.Contains(p.Key)) || RoutingKeys.Any(k => !obj.ContainsKey(k))) throw new InvalidDataException("Unexpected checkpoint settings.");
        var cfg = obj.Deserialize<CehoConfig>() ?? throw new InvalidDataException("Invalid settings.");
        cfg.Validate();
        return cfg;
    }

    private static bool NeedsSecurityAcknowledgement(string root, Snapshot snapshot)
    {
        var target = ParseConfig(snapshot);
        var path = Path.Combine(root, "config.json");
        CehoConfig current;
        try { current = File.Exists(path) ? JsonSerializer.Deserialize<CehoConfig>(ReadBounded(path))! : new(); }
        catch { return true; }
        if (current is null || current.FailClosed && !target.FailClosed || current.TunIpv6 && !target.TunIpv6) return true;
        var targetInsecure = InsecureEndpoints(snapshot.Runtime);
        if (targetInsecure.Count == 0) return false;
        try
        {
            var live = Path.Combine(root, "singbox.json");
            return !File.Exists(live) || !targetInsecure.IsSubsetOf(InsecureEndpoints(ReadBounded(live)));
        }
        catch { return true; }
    }

    private static HashSet<string> InsecureEndpoints(string runtime)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var obj = JsonNode.Parse(runtime)!.AsObject();
        if (obj["outbounds"] is JsonArray outbounds)
            foreach (var item in outbounds.OfType<JsonObject>())
                if (item["tls"]?["insecure"]?.GetValue<bool>() == true)
                    result.Add($"{item["type"]}|{item["server"]}|{item["server_port"]}|{item["tls"]?["server_name"]}");
        return result;
    }

    private static void ValidateRuntime(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid generated rules.");
    }

    private static string ReadBounded(string path) => PrivateFile.ReadText(path, MaxBytes);

    private static void EnsureSize(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes)
            throw new InvalidDataException("Configuration file is too large.");
    }

    private static bool IsCacheName(string name) => name.StartsWith("sub-", StringComparison.Ordinal)
        && name.EndsWith(".txt", StringComparison.Ordinal) && name.Length > 8
        && CehoConfig.IsSafeSubscriptionName(name[4..^4]) && Path.GetFileName(name) == name;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

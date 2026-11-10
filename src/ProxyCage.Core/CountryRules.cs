using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ProxyCage.Core;

public sealed record CountryOption(string Code, string NameRu, string NameEn, string? DomainAsset = null)
{
    public string IpAsset => $"geoip-{Code}.srs";
    public bool HasDomains => DomainAsset is not null;
}

public sealed record CountryRuleSet(string Tag, string Path, bool Domains);

/// <summary>
/// Страна пользователя: её ресурсы открываются напрямую, без туннеля.
/// Наборы правил те же, что в приложении для Android, лежат внутри программы.
/// </summary>
public static class CountryRules
{
    public static readonly IReadOnlyList<CountryOption> Options = new[]
    {
        new CountryOption("ru", "Россия", "Russia", "geosite-category-ru.srs"),
        new CountryOption("cn", "Китай", "China", "geosite-cn.srs"),
        new CountryOption("ir", "Иран", "Iran", "geosite-category-ir.srs"),
        new CountryOption("de", "Германия", "Germany"),
        new CountryOption("us", "США", "United States"),
        new CountryOption("gb", "Великобритания", "United Kingdom"),
        new CountryOption("kz", "Казахстан", "Kazakhstan"),
        new CountryOption("by", "Беларусь", "Belarus"),
        new CountryOption("tr", "Турция", "Turkey"),
        new CountryOption("ua", "Украина", "Ukraine"),
    };

    private static readonly object Gate = new();

    public static CountryOption? Find(string? code)
        => Options.FirstOrDefault(o => o.Code.Equals((code ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? code) => Find(code)?.Code ?? "";

    public static string DefaultDir(string root) => Path.Combine(root, "country-rules");

    /// <summary>Раскладывает на диск наборы выбранной страны и возвращает их для конфигурации движка.</summary>
    public static List<CountryRuleSet> Prepare(string? code, string directory)
    {
        var country = Find(code);
        if (country is null) return new();
        lock (Gate)
        {
            var expected = Manifest();
            Directory.CreateDirectory(directory);
            var result = new List<CountryRuleSet>();
            foreach (var name in new[] { country.IpAsset, country.DomainAsset }.OfType<string>())
            {
                if (!expected.TryGetValue(name, out var checksum))
                    throw new InvalidOperationException($"В программе нет набора правил {name}.");
                var path = Path.Combine(directory, name);
                if (!File.Exists(path) || Sha256(File.ReadAllBytes(path)) != checksum)
                {
                    var data = Read("country-rules/" + name);
                    if (Sha256(data) != checksum)
                        throw new InvalidOperationException($"Повреждён набор правил {name}.");
                    var temporary = path + ".tmp";
                    File.WriteAllBytes(temporary, data);
                    File.Move(temporary, path, overwrite: true);
                }
                var domains = name == country.DomainAsset;
                result.Add(new CountryRuleSet($"country-{country.Code}-{(domains ? "domains" : "ip")}", path, domains));
            }
            return result;
        }
    }

    private static Dictionary<string, string> Manifest()
    {
        using var doc = JsonDocument.Parse(Read("country-rules/manifest.json"));
        return doc.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("name").GetString()!, f => f.GetProperty("sha256").GetString()!);
    }

    private static byte[] Read(string resource)
    {
        using var stream = typeof(CountryRules).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"В программе нет файла {resource}.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

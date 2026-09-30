using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ProxyCage.Core;

public static class SettingsTransfer
{
    public const string Header = "CehoProxy settings v1";
    public const string FileName = "cehoproxy-settings.chps";
    private const int Iterations = 200_000;

    public sealed class WrongPasswordException : Exception
    {
        public WrongPasswordException() : base("wrong password or damaged file") { }
    }

    public static string Export(string root, string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("password");
        var subs = new JsonObject();
        foreach (var file in Directory.EnumerateFiles(root, "sub-*.txt"))
            subs[Path.GetFileName(file)] = File.ReadAllText(file);
        var payload = new JsonObject
        {
            ["config"] = File.ReadAllText(Path.Combine(root, "config.json")),
            ["subs"] = subs,
        }.ToJsonString();
        return Header + "\n" + Convert.ToBase64String(Seal(Encoding.UTF8.GetBytes(payload), password)) + "\n";
    }

    public static int Import(string root, string text, string password)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2 || lines[0].Trim() != Header) throw new InvalidDataException("not a CehoProxy settings file");
        byte[] sealedBytes;
        try { sealedBytes = Convert.FromBase64String(string.Concat(lines.Skip(1)).Trim()); }
        catch (FormatException) { throw new InvalidDataException("damaged settings file"); }

        var payload = JsonNode.Parse(Encoding.UTF8.GetString(Open(sealedBytes, password)))!.AsObject();
        var configText = payload["config"]!.GetValue<string>();

        var configPath = Path.Combine(root, "config.json");
        var probe = Path.Combine(root, "config.import.json");
        File.WriteAllText(probe, configText);
        try
        {
            var imported = CehoConfig.Load(probe);
            if (File.Exists(configPath))
            {
                var local = CehoConfig.Load(configPath);
                imported.WebPort = local.WebPort;
                imported.MixedPort = local.MixedPort;
            }
            imported.Save(configPath);
        }
        finally { try { File.Delete(probe); } catch { } }

        var count = 0;
        if (payload["subs"] is JsonObject subs)
            foreach (var (name, value) in subs)
            {
                if (value is null || !IsSubFileName(name)) continue;
                File.WriteAllText(Path.Combine(root, name), value.GetValue<string>());
                count++;
            }
        Auth.RestrictConfigAccess(configPath);
        return count;
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

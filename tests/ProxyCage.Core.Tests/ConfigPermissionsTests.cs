namespace ProxyCage.Core.Tests;

public sealed class ConfigPermissionsTests
{
    [Fact]
    public void Secrets_in_a_directory_with_spaces_are_private()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "ceho permissions " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var config = Path.Combine(root, "config.json");
            var cache = Path.Combine(root, "sub-test.txt");
            foreach (var path in new[] { config, cache })
            {
                File.WriteAllText(path, "test");
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
            }
            Auth.RestrictConfigAccess(config);
            foreach (var path in new[] { config, cache })
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally { Directory.Delete(root, true); }
    }
}

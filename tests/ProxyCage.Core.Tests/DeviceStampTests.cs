using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class DeviceStampTests
{
    [Fact]
    public void Stamp_is_stable_and_accepted_by_remnawave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ceho-hwid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var first = DeviceStamp.LoadOrCreate(dir);
            var again = DeviceStamp.LoadOrCreate(dir);
            Assert.Equal(first, again);
            Assert.True(DeviceStamp.IsValid(first));
            Assert.StartsWith("ceho", first);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

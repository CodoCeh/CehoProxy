using ProxyCage.Core;
using Xunit;

namespace ProxyCage.Core.Tests;

public class WintunDisabledFlagTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(0x21, true)]
    [InlineData(0x20, false)]
    public void Only_the_disabled_bit_counts(int flags, bool expected) =>
        Assert.Equal(expected, TunCleanup.ConfigFlagsDisabled(flags));

    [Fact]
    public void Missing_value_is_not_disabled() =>
        Assert.False(TunCleanup.ConfigFlagsDisabled(null));
}

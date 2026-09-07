using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class SubscriptionInfoTests
{
    [Fact]
    public void Reads_expiry_and_traffic_from_header()
    {
        var info = SubscriptionInfo.FromHeader(
            "upload=1024; download=2048; total=10737418240; expire=1789000000");

        Assert.NotNull(info);
        Assert.Equal(3072, info!.UsedBytes);
        Assert.Equal(10737418240, info.TotalBytes);
        Assert.Equal(new DateTime(2026, 9, 10, 0, 26, 40, DateTimeKind.Utc), info.ExpiresUtc);
    }

    [Fact]
    public void Zero_means_unlimited_not_empty_quota()
    {
        var info = SubscriptionInfo.FromHeader("upload=0; download=500; total=0; expire=0");

        Assert.NotNull(info);
        Assert.Null(info!.TotalBytes);
        Assert.Null(info.ExpiresUtc);
        Assert.Equal(500, info.UsedBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense without pairs")]
    public void Nothing_useful_gives_nothing(string? header) =>
        Assert.Null(SubscriptionInfo.FromHeader(header));

    [Fact]
    public void Header_without_numbers_is_not_treated_as_data() =>
        Assert.Null(SubscriptionInfo.FromHeader("upload=abc; total=xyz"));

    [Theory]
    [InlineData("Expire: 2026-09-01", 2026, 9, 1)]
    [InlineData("套餐到期：2027-01-15", 2027, 1, 15)]
    [InlineData("подписка действует до 01.03.2027", 2027, 3, 1)]
    [InlineData("valid until 2026/12/31", 2026, 12, 31)]
    public void Reads_expiry_from_node_remark(string remark, int year, int month, int day)
    {
        var found = SubscriptionInfo.ExpiryFromRemarks(new[] { "DE-01", remark });

        Assert.Equal(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc), found);
    }

    [Fact]
    public void Numbers_in_node_names_are_not_dates() =>
        Assert.Null(SubscriptionInfo.ExpiryFromRemarks(new[]
        {
            "NL 2026 10.05", "DE-01 | 100 GB", "US 2026/09/07 node",
        }));

    [Fact]
    public void Impossible_date_next_to_the_right_word_is_ignored() =>
        Assert.Null(SubscriptionInfo.ExpiryFromRemarks(new[] { "expire 2026-13-45" }));

    [Theory]
    [InlineData(512, "512 Б")]
    [InlineData(2048, "2.0 КБ")]
    [InlineData(10737418240, "10.0 ГБ")]
    [InlineData(214748364800, "200 ГБ")]
    public void Bytes_read_like_a_human_wrote_them(long value, string expected) =>
        Assert.Equal(expected, SubscriptionInfo.Bytes(value));

    [Fact]
    public void Days_left_goes_negative_after_expiry()
    {
        Assert.True(SubscriptionInfo.DaysLeft(DateTime.UtcNow.AddDays(5)) is 4 or 5);
        Assert.True(SubscriptionInfo.DaysLeft(DateTime.UtcNow.AddDays(-2)) < 0);
    }
}

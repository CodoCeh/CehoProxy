namespace ProxyCage.Core.Tests;

/// <summary>
/// Все примеры взяты из настоящих подписей нод. Каждый случай, помеченный «поймано живьём»,
/// раньше давал неверную страну или выбрасывал ноду из пула.
/// </summary>
public class CountryResolverTests
{
    [Theory]
    [InlineData("🇳🇱 Нидерланды-2", "NL")]
    [InlineData("🇫🇮 Финляндия-1", "FI")]
    [InlineData("🇷🇺 Россия-PRO (Ютуб без рекламы)", "RU")]
    public void Reads_flag_emoji(string remark, string expected)
        => Assert.Equal(expected, CountryResolver.ResolveCode(remark));

    [Theory]
    [InlineData("Германия #3", "DE")]
    [InlineData("Австрия-1", "AT")]
    [InlineData("Испания", "ES")]
    [InlineData("ОАЭ Дубай", "AE")]
    [InlineData("ЮАР", "ZA")]
    [InlineData("Кыргызстан", "KG")]
    public void Reads_russian_names(string remark, string expected)
        => Assert.Equal(expected, CountryResolver.ResolveCode(remark));

    [Theory]
    [InlineData("Germany-Frankfurt-01", "DE")]
    [InlineData("United Arab Emirates", "AE")]
    [InlineData("Deutschland Nord", "DE")]
    [InlineData("Türkiye 2", "TR")]
    [InlineData("España", "ES")]
    public void Reads_english_and_native_names(string remark, string expected)
        => Assert.Equal(expected, CountryResolver.ResolveCode(remark));

    [Theory]
    [InlineData("DE-01", "DE")]
    [InlineData("[NL] node2", "NL")]
    [InlineData("US | Dallas", "US")]
    [InlineData("UK London", "GB")]
    public void Reads_country_code_as_a_separate_word(string remark, string expected)
        => Assert.Equal(expected, CountryResolver.ResolveCode(remark));

    [Theory]
    [InlineData("Ukraine", "UA")]        // ловилось на подстроку «uk» и давало GB
    [InlineData("Fukuoka JP-2", "JP")]   // то же самое
    [InlineData("Nigeria-1", "NG")]      // ловилось на «Niger» и давало NE
    public void Does_not_match_inside_a_word(string remark, string expected)
        => Assert.Equal(expected, CountryResolver.ResolveCode(remark));

    [Theory]
    [InlineData("node 100 GB трафика")]  // объём трафика, а не Великобритания
    [InlineData("Smart TV профиль")]     // телевизор, а не Тувалу
    [InlineData("Ключ для роутера 5 Gbit/s")]
    public void Leaves_country_unknown_when_there_is_none(string remark)
        => Assert.Null(CountryResolver.ResolveCode(remark));

    [Fact]
    public void Shows_country_name_in_the_interface_language()
    {
        Assert.Equal("Нидерланды", CountryResolver.DisplayName("NL"));
        Assert.Equal("Netherlands", CountryResolver.DisplayName("NL", "en"));
        Assert.Equal("Южная Корея", CountryResolver.DisplayName("KR"));
    }

    [Fact]
    public void Falls_back_to_the_code_when_the_country_is_unknown()
    {
        Assert.Null(CountryResolver.DisplayName(null));
        Assert.Equal("ZZ", CountryResolver.DisplayName("ZZ"));
    }

    [Fact]
    public void Builds_a_flag_from_the_code()
    {
        Assert.Equal("🇳🇱", CountryResolver.Flag("NL"));
        Assert.Equal("", CountryResolver.Flag("?"));
    }
}

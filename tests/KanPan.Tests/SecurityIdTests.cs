using KanPan.Core.Models;

namespace KanPan.Tests;

public class SecurityIdTests
{
    [Theory]
    [InlineData("600519", "sh600519")]
    [InlineData("688981", "sh688981")]
    [InlineData("510300", "sh510300")]
    [InlineData("113050", "sh113050")]
    [InlineData("900901", "sh900901")]
    [InlineData("000001", "sz000001")]
    [InlineData("300750", "sz300750")]
    [InlineData("159915", "sz159915")]
    [InlineData("200002", "sz200002")]
    [InlineData("430047", "bj430047")]
    [InlineData("830799", "bj830799")]
    [InlineData("920001", "bj920001")]
    public void Infers_exchange_from_bare_code(string raw, string expected) =>
        Assert.Equal(expected, SecurityId.Parse(raw).Symbol);

    [Theory]
    [InlineData("sh600519", "sh600519")]
    [InlineData("SZ000001", "sz000001")]
    [InlineData(" 600519.SH ", "sh600519")]
    [InlineData("000001.sz", "sz000001")]
    [InlineData("sz600519", "sz600519")]
    public void Explicit_market_wins(string raw, string expected) =>
        Assert.Equal(expected, SecurityId.Parse(raw).Symbol);

    [Theory]
    [InlineData("00700", "hk00700")]
    [InlineData("700", "hk00700")]
    [InlineData("5", "hk00005")]
    [InlineData("09988", "hk09988")]
    [InlineData("hk00700", "hk00700")]
    [InlineData("HK700", "hk00700")]
    [InlineData("0700.HK", "hk00700")]
    [InlineData("02800.hk", "hk02800")]
    public void Hong_Kong_codes_are_padded_to_five_digits(string raw, string expected)
    {
        var id = SecurityId.Parse(raw);
        Assert.Equal(expected, id.Symbol);
        Assert.True(id.IsHongKong);
        Assert.Equal(MarketGroup.HongKong, id.Group);
        Assert.Equal("港元", id.CurrencyUnit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("6005190")]
    [InlineData("sh60051")]
    [InlineData("hk600519")]
    [InlineData("00000")]
    [InlineData("760001")]
    public void Rejects_invalid(string raw)
    {
        Assert.False(SecurityId.TryParse(raw, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Hong_Kong_error_explains_the_format()
    {
        SecurityId.TryParse("hk600519", out _, out var error);
        Assert.Contains("港股", error);
    }
}

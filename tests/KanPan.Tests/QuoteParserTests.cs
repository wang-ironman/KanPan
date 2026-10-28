using KanPan.Core.Models;
using KanPan.Infrastructure.Quotes;

namespace KanPan.Tests;

/// <summary>Fixtures are real responses captured on 2026-10-21 after the close.</summary>
public class QuoteParserTests
{
    private static string Fixture(string name) =>
        HttpQuoteProvider.Gbk.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    private static Dictionary<string, Quote> ParseTencent() =>
        TencentQuoteProvider.ParseBody(Fixture("tencent_a.txt")).ToDictionary(q => q.Id.Symbol);

    private static Dictionary<string, Quote> ParseSina() =>
        SinaQuoteProvider.ParseBody(Fixture("sina_a.txt")).ToDictionary(q => q.Id.Symbol);

    [Fact]
    public void Tencent_stock()
    {
        var q = ParseTencent()["sh600519"];
        Assert.Equal("贵州茅台", q.Name);
        Assert.Equal(1309.69m, q.Price);
        Assert.Equal(1331.88m, q.PrevClose);
        Assert.Equal(new DateTime(2026, 10, 21, 15, 26, 23), q.QuoteTime);
        Assert.Equal(-1.67m, Math.Round(q.ChangePct, 2));
    }

    [Fact]
    public void Tencent_etf_keeps_three_decimals()
    {
        var q = ParseTencent()["sh510300"];
        Assert.Equal(4.185m, q.Price);
        Assert.Equal(4.220m, q.PrevClose);
    }

    [Fact]
    public void Tencent_omits_unknown_codes()
    {
        var quotes = ParseTencent();
        Assert.Equal(5, quotes.Count);
        Assert.False(quotes.ContainsKey("sh999999"));
        Assert.True(quotes.ContainsKey("bj430047"));
    }

    [Fact]
    public void Sina_stock()
    {
        var q = ParseSina()["sz000001"];
        Assert.Equal("平安银行", q.Name);
        Assert.Equal(11.41m, q.Price);
        Assert.Equal(11.44m, q.PrevClose);
        Assert.Equal(new DateTime(2026, 10, 21, 15, 26, 0), q.QuoteTime);
    }

    [Fact]
    public void Sina_drops_empty_and_all_zero_quotes()
    {
        var quotes = ParseSina();
        Assert.False(quotes.ContainsKey("sh999999"));
        Assert.False(quotes.ContainsKey("bj430047"));
        Assert.Equal(4, quotes.Count);
    }

    [Fact]
    public void Both_sources_agree()
    {
        var tencent = ParseTencent();
        foreach (var (symbol, sina) in ParseSina())
        {
            Assert.Equal(sina.Price, tencent[symbol].Price);
            Assert.Equal(sina.PrevClose, tencent[symbol].PrevClose);
        }
    }

    // Hong Kong fixtures: captured 2026-10-22 16:16, after the closing auction, mixed with one A share.

    [Fact]
    public void Tencent_hong_kong_stock()
    {
        var quotes = TencentQuoteProvider.ParseBody(Fixture("tencent_hk.txt")).ToDictionary(q => q.Id.Symbol);
        var q = quotes["hk00700"];
        Assert.Equal("腾讯控股", q.Name);
        Assert.Equal(382.4m, q.Price);
        Assert.Equal(393.8m, q.PrevClose);
        Assert.Equal(new DateTime(2026, 10, 22, 16, 8, 40), q.QuoteTime);
        Assert.Equal(-2.89m, Math.Round(q.ChangePct, 2));
        Assert.Equal("382.40", Core.Display.DisplayFormat.Price(q.Price));

        Assert.Equal(23.58m, quotes["hk02800"].Price);
        Assert.False(quotes.ContainsKey("hk99999"));
        Assert.Equal(1318.49m, quotes["sh600519"].Price);
    }

    [Fact]
    public void Sina_hong_kong_stock()
    {
        var quotes = SinaQuoteProvider.ParseBody(Fixture("sina_hk.txt")).ToDictionary(q => q.Id.Symbol);
        var q = quotes["hk00700"];
        Assert.Equal("腾讯控股", q.Name);
        Assert.Equal(382.4m, q.Price);
        Assert.Equal(393.8m, q.PrevClose);
        Assert.Equal(new DateTime(2026, 10, 22, 16, 8, 40), q.QuoteTime);

        Assert.Equal(4, quotes.Count);
        Assert.Equal(1318.49m, quotes["sh600519"].Price);
    }

    [Fact]
    public void Hong_kong_sources_agree()
    {
        var tencent = TencentQuoteProvider.ParseBody(Fixture("tencent_hk.txt")).ToDictionary(q => q.Id);
        foreach (var sina in SinaQuoteProvider.ParseBody(Fixture("sina_hk.txt")))
        {
            Assert.Equal(sina.Price, tencent[sina.Id].Price);
            Assert.Equal(sina.PrevClose, tencent[sina.Id].PrevClose);
        }
    }

    [Fact]
    public void Exchange_rates()
    {
        Assert.Equal(0.8505m, TencentQuoteProvider.ParseFxBody(Fixture("tencent_fx.txt")));
        Assert.Equal(0.8509m, SinaQuoteProvider.ParseFxBody(Fixture("sina_fx.txt")));
    }

    [Fact]
    public void Missing_or_absurd_exchange_rate_throws()
    {
        Assert.Throws<FormatException>(() => TencentQuoteProvider.ParseFxBody("v_pv_none_match=\"1\";"));
        Assert.Throws<FormatException>(() => SinaQuoteProvider.ParseFxBody("var hq_str_fx_shkdcny=\"\";"));
        Assert.Throws<FormatException>(() => TencentQuoteProvider.ParseFxBody("v_whHKDCNY=\"310~港元人民币~HKDCNY~7.85~0\";"));
    }

    [Fact]
    public void Malformed_numbers_throw() =>
        Assert.Throws<FormatException>(() => TencentQuoteProvider.ParseBody(
            "v_sh600519=\"1~贵州茅台~600519~N/A~1331.88" + string.Concat(Enumerable.Repeat("~0", 30)) + "\";"));

    [Fact]
    public void Garbage_body_yields_nothing() =>
        Assert.Empty(TencentQuoteProvider.ParseBody("<html>blocked</html>"));
}

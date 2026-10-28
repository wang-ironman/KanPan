using KanPan.Core.Display;
using KanPan.Core.Models;

namespace KanPan.Tests;

public class DisplayFormatTests
{
    [Theory]
    [InlineData("0", "0.0")]
    [InlineData("1.28", "1.2")]
    [InlineData("-1.28", "1.2")]
    [InlineData("4.99", "4.9")]
    [InlineData("9.99", "9.9")]
    [InlineData("10", "10")]
    [InlineData("-19.9", "19")]
    [InlineData("150", "99")]
    public void Tray_badge(string pct, string expected) =>
        Assert.Equal(expected, DisplayFormat.TrayBadge(decimal.Parse(pct)));

    [Theory]
    [InlineData("1309.69", "1,309.69")]
    [InlineData("4.185", "4.185")]
    [InlineData("4.220", "4.220")]
    [InlineData("11.4", "11.40")]
    [InlineData("1.23456", "1.235")]
    public void Price_keeps_source_precision(string price, string expected) =>
        Assert.Equal(expected, DisplayFormat.Price(decimal.Parse(price)));

    [Fact]
    public void Signed_values()
    {
        Assert.Equal("+3,256.00", DisplayFormat.SignedAmount(3256m));
        Assert.Equal("-12.50", DisplayFormat.SignedAmount(-12.5m));
        Assert.Equal("0.00", DisplayFormat.SignedAmount(0m));
        Assert.Equal("+1.28%", DisplayFormat.SignedPct(1.2849m));
    }

    [Fact]
    public void Day_label_names_earlier_session()
    {
        var today = new DateOnly(2026, 10, 21);
        Assert.Equal("当日盈亏", DisplayFormat.DayLabel(today, today));
        Assert.Equal("当日盈亏", DisplayFormat.DayLabel(null, today));
        Assert.Equal("10-16 盈亏", DisplayFormat.DayLabel(new DateOnly(2026, 10, 16), today));
    }

    [Fact]
    public void Tooltip_fits_windows_limit()
    {
        var now = new DateTime(2026, 10, 21, 14, 32, 5);
        var holding = new Holding(SecurityId.Parse("600519"), 100, null);
        var view = new HoldingView(holding, null, 0, 0, 0, null, null, false);
        var snapshot = new PortfolioSnapshot(
            [view], 123_456_789_012.34m, -98_765_432.10m, -12.34m, null, null, 0,
            DateOnly.FromDateTime(now), MarketPhase.Trading, now, now.AddMinutes(-5), true);

        var tip = DisplayFormat.TrayTooltip(snapshot, DateOnly.FromDateTime(now), alertsPaused: true);
        Assert.True(tip.Length <= 127);
        Assert.StartsWith("KanPan  14:32:05  交易中\n当日盈亏：-98,765,432.10 (-12.34%)", tip);
    }
}

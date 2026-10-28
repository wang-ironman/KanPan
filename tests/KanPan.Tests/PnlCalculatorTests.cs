using KanPan.Core.Models;
using KanPan.Core.Pnl;

namespace KanPan.Tests;

public class PnlCalculatorTests
{
    private static readonly DateTime Today = new(2026, 10, 21, 14, 30, 0);
    private static readonly SecurityId A = SecurityId.Parse("600519");
    private static readonly SecurityId B = SecurityId.Parse("000001");

    private static PortfolioSnapshot Calc(IReadOnlyList<Holding> holdings, params Quote[] quotes) =>
        PnlCalculator.Calculate(holdings, quotes.ToDictionary(q => q.Id), MarketPhase.Trading, Today, Today, false);

    [Fact]
    public void Day_pct_uses_previous_close_value_as_base()
    {
        var snapshot = Calc(
            [new(A, 100, null), new(B, 1000, null)],
            new Quote(A, "A", 110m, 100m, Today),
            new Quote(B, "B", 9m, 10m, Today));

        // A: +1000 on base 10000; B: -1000 on base 10000
        Assert.Equal(0m, snapshot.DayPnl);
        Assert.Equal(0m, snapshot.DayPct);
        Assert.Equal(11000m + 9000m, snapshot.TotalMarketValue);
        Assert.Equal(10m, snapshot.Items[0].DayPct);
        Assert.Equal(-10m, snapshot.Items[1].DayPct);
    }

    [Fact]
    public void Day_pct_is_not_diluted_by_todays_gain()
    {
        var snapshot = Calc([new(A, 100, null)], new Quote(A, "A", 110m, 100m, Today));
        Assert.Equal(1000m, snapshot.DayPnl);
        Assert.Equal(10m, snapshot.DayPct);
    }

    [Fact]
    public void Total_pnl_only_counts_holdings_with_cost()
    {
        var snapshot = Calc(
            [new(A, 100, 80m), new(B, 1000, null)],
            new Quote(A, "A", 110m, 100m, Today),
            new Quote(B, "B", 9m, 10m, Today));

        Assert.Equal(3000m, snapshot.TotalPnl);
        Assert.Equal(37.5m, snapshot.TotalPct);
        Assert.Equal(1, snapshot.CostCoveredCount);
        Assert.Null(snapshot.Items[1].TotalPnl);
    }

    [Fact]
    public void Non_positive_diluted_cost_has_pnl_but_no_pct()
    {
        var snapshot = Calc([new(A, 100, -10m)], new Quote(A, "A", 110m, 100m, Today));
        Assert.Equal(12000m, snapshot.Items[0].TotalPnl);
        Assert.Null(snapshot.Items[0].TotalPct);
        Assert.Equal(12000m, snapshot.TotalPnl);
        Assert.Null(snapshot.TotalPct);
    }

    [Fact]
    public void No_cost_means_no_total()
    {
        var snapshot = Calc([new(A, 100, null)], new Quote(A, "A", 110m, 100m, Today));
        Assert.Null(snapshot.TotalPnl);
        Assert.Null(snapshot.TotalPct);
    }

    [Fact]
    public void Missing_quote_is_listed_but_excluded()
    {
        var snapshot = Calc([new(A, 100, 50m), new(B, 1000, null)], new Quote(A, "A", 110m, 100m, Today));

        Assert.Equal(2, snapshot.Items.Count);
        Assert.Null(snapshot.Items[1].Quote);
        Assert.Equal(11000m, snapshot.TotalMarketValue);
        Assert.Equal(10m, snapshot.DayPct);
    }

    [Fact]
    public void Quote_older_than_session_contributes_value_but_no_day_pnl()
    {
        var snapshot = Calc(
            [new(A, 100, null), new(B, 1000, null)],
            new Quote(A, "A", 110m, 100m, Today),
            new Quote(B, "B", 9m, 10m, Today.AddDays(-30)));

        Assert.Equal(1000m, snapshot.DayPnl);
        Assert.Equal(1000m / (10000m + 9000m) * 100m, snapshot.DayPct);
        Assert.False(snapshot.Items[1].IsCurrent);
        Assert.Equal(0m, snapshot.Items[1].DayPct);
    }

    [Fact]
    public void Weekend_shows_last_session()
    {
        var friday = new DateTime(2026, 10, 16, 15, 0, 0);
        var snapshot = Calc([new(A, 100, null)], new Quote(A, "A", 110m, 100m, friday));

        Assert.Equal(DateOnly.FromDateTime(friday), snapshot.SessionDate);
        Assert.Equal(1000m, snapshot.DayPnl);
    }

    [Fact]
    public void Suspended_quote_has_zero_change()
    {
        var q = Quote.Create(A, "A", 0m, 100m, Today)!;
        Assert.True(q.IsSuspended);
        Assert.Equal(100m, q.Price);

        var snapshot = Calc([new(A, 100, null)], q);
        Assert.Equal(0m, snapshot.DayPnl);
        Assert.Equal(10000m, snapshot.TotalMarketValue);
    }

    [Fact]
    public void Quote_without_previous_close_is_dropped() =>
        Assert.Null(Quote.Create(A, "A", 0m, 0m, Today));

    private static readonly SecurityId Hk = SecurityId.Parse("00700");

    [Fact]
    public void Hong_kong_amounts_are_converted_to_cny()
    {
        var snapshot = PnlCalculator.Calculate(
            [new(A, 100, null), new(Hk, 200, 350m)],
            new[] { new Quote(A, "A", 110m, 100m, Today), new Quote(Hk, "腾讯", 400m, 380m, Today) }.ToDictionary(q => q.Id),
            MarketPhase.Trading, Today, Today, false, hkdCny: 0.85m);

        var hk = snapshot.Items[1];
        Assert.Equal(400m * 200 * 0.85m, hk.MarketValue);
        Assert.Equal(20m * 200 * 0.85m, hk.DayPnl);
        Assert.Equal(50m * 200 * 0.85m, hk.TotalPnl);
        Assert.Equal(50m / 350m * 100m, hk.TotalPct!.Value, 10);
        Assert.Equal(400m / 380m * 100m - 100m, hk.DayPct, 10);
        Assert.Equal(0.85m, hk.FxRate);

        Assert.Equal(11000m + 68000m, snapshot.TotalMarketValue);
        Assert.Equal(1000m + 3400m, snapshot.DayPnl);
        Assert.Equal(4400m / (10000m + 64600m) * 100m, snapshot.DayPct, 10);
        Assert.Equal(0.85m, snapshot.HkdCnyRate);
        Assert.True(snapshot.HasHongKong);
    }

    [Fact]
    public void Hong_kong_without_rate_is_listed_but_excluded()
    {
        var snapshot = PnlCalculator.Calculate(
            [new(A, 100, null), new(Hk, 200, 350m)],
            new[] { new Quote(A, "A", 110m, 100m, Today), new Quote(Hk, "腾讯", 400m, 380m, Today) }.ToDictionary(q => q.Id),
            MarketPhase.Trading, Today, Today, false, hkdCny: null);

        var hk = snapshot.Items[1];
        Assert.NotNull(hk.Quote);
        Assert.Null(hk.FxRate);
        Assert.Equal(0m, hk.MarketValue);
        Assert.Null(hk.TotalPnl);
        Assert.Equal(11000m, snapshot.TotalMarketValue);
        Assert.Null(snapshot.TotalPnl);
    }

    [Fact]
    public void Market_closed_for_holiday_adds_no_day_pnl()
    {
        // Hong Kong closed today; its last quote is from yesterday while A shares trade.
        var snapshot = PnlCalculator.Calculate(
            [new(A, 100, null), new(Hk, 200, null)],
            new[] { new Quote(A, "A", 110m, 100m, Today), new Quote(Hk, "腾讯", 400m, 380m, Today.AddDays(-1)) }.ToDictionary(q => q.Id),
            MarketPhase.Trading, Today, Today, false, hkdCny: 0.85m);

        Assert.False(snapshot.Items[1].IsCurrent);
        Assert.Equal(1000m, snapshot.DayPnl);
        Assert.Equal(11000m + 68000m, snapshot.TotalMarketValue);
    }

    [Fact]
    public void Empty_portfolio()
    {
        var snapshot = Calc([]);
        Assert.Equal(0m, snapshot.DayPct);
        Assert.Null(snapshot.SessionDate);
    }
}

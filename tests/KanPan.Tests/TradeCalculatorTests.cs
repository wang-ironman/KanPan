using KanPan.Core.Models;
using KanPan.Core.Trades;

namespace KanPan.Tests;

public class TradeCalculatorTests
{
    private static readonly SecurityId A = SecurityId.Parse("600519");

    private static TradeResult Apply(Holding? before, TradeSide side, long qty, decimal price, decimal fee = 0,
        CostMethod method = CostMethod.Diluted) =>
        TradeCalculator.Apply(before, new Trade(A, side, qty, price, fee), method);

    [Fact]
    public void Opening_a_position_uses_price_plus_fee()
    {
        var r = Apply(null, TradeSide.Buy, 100, 10m, fee: 5m);
        Assert.Equal(new Holding(A, 100, 10.05m), r.After);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void Buying_more_averages_cost()
    {
        var r = Apply(new Holding(A, 200, 1500m), TradeSide.Buy, 100, 1200m);
        Assert.Equal(300, r.After!.Shares);
        Assert.Equal(1400m, r.After.CostPrice);
    }

    [Fact]
    public void Cost_is_rounded_to_four_decimals()
    {
        var r = Apply(new Holding(A, 200, 10m), TradeSide.Buy, 100, 11m);
        Assert.Equal(10.3333m, r.After!.CostPrice);
    }

    [Fact]
    public void Buying_without_existing_cost_keeps_cost_unknown()
    {
        var r = Apply(new Holding(A, 200, null), TradeSide.Buy, 100, 10m);
        Assert.Equal(new Holding(A, 300, null), r.After);
        Assert.NotNull(r.Warning);
    }

    [Fact]
    public void Diluted_sell_lowers_cost_by_proceeds()
    {
        // 200 @ 100 = 20000 invested; sell 100 @ 120 (fee 10) returns 11990; 8010 left on 100 shares
        var r = Apply(new Holding(A, 200, 100m), TradeSide.Sell, 100, 120m, fee: 10m);
        Assert.Equal(new Holding(A, 100, 80.1m), r.After);
    }

    [Fact]
    public void Weighted_average_sell_keeps_cost()
    {
        var r = Apply(new Holding(A, 200, 100m), TradeSide.Sell, 100, 120m, fee: 10m, CostMethod.WeightedAverage);
        Assert.Equal(new Holding(A, 100, 100m), r.After);
    }

    [Fact]
    public void Methods_agree_on_buys()
    {
        var before = new Holding(A, 200, 100m);
        Assert.Equal(
            Apply(before, TradeSide.Buy, 100, 90m, 5m, CostMethod.Diluted).After,
            Apply(before, TradeSide.Buy, 100, 90m, 5m, CostMethod.WeightedAverage).After);
    }

    [Fact]
    public void Diluted_cost_can_reach_zero_or_below_with_warning()
    {
        var r = Apply(new Holding(A, 200, 10m), TradeSide.Sell, 100, 30m);
        Assert.Equal(-10m, r.After!.CostPrice);
        Assert.Contains("本金", r.Warning);
    }

    [Fact]
    public void Selling_everything_closes_the_position()
    {
        var r = Apply(new Holding(A, 200, 10m), TradeSide.Sell, 200, 12m);
        Assert.True(r.IsValid);
        Assert.Null(r.After);
    }

    [Fact]
    public void Selling_without_cost_only_reduces_shares()
    {
        var r = Apply(new Holding(A, 200, null), TradeSide.Sell, 50, 12m);
        Assert.Equal(new Holding(A, 150, null), r.After);
        Assert.NotNull(r.Warning);
    }

    [Theory]
    [InlineData(TradeSide.Sell, 300, 10, 0, "超过持仓")]
    [InlineData(TradeSide.Buy, 0, 10, 0, "数量")]
    [InlineData(TradeSide.Buy, 100, 0, 0, "价格")]
    [InlineData(TradeSide.Buy, 100, 10, -1, "手续费")]
    public void Rejects_invalid_trades(TradeSide side, long qty, decimal price, decimal fee, string messagePart)
    {
        var r = Apply(new Holding(A, 200, 10m), side, qty, price, fee);
        Assert.False(r.IsValid);
        Assert.Contains(messagePart, r.Error);
    }

    [Fact]
    public void Cannot_sell_what_you_do_not_hold() =>
        Assert.Contains("没有", Apply(null, TradeSide.Sell, 100, 10m).Error);

    [Fact]
    public void Mismatched_holding_throws() =>
        Assert.Throws<ArgumentException>(() =>
            TradeCalculator.Apply(new Holding(SecurityId.Parse("000001"), 1, 1m), new Trade(A, TradeSide.Buy, 1, 1m), CostMethod.Diluted));
}

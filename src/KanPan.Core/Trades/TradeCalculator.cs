using KanPan.Core.Models;

namespace KanPan.Core.Trades;

public enum TradeSide { Buy, Sell }

/// <summary>How a sell affects the cost price. Buys are identical under both.</summary>
public enum CostMethod
{
    /// <summary>Sale proceeds reduce the remaining cost, so realized gains lower the cost price (can go to zero or below).</summary>
    Diluted,

    /// <summary>A sell only reduces shares; the per-share cost is unchanged.</summary>
    WeightedAverage,
}

public sealed record Trade(SecurityId Id, TradeSide Side, long Quantity, decimal Price, decimal Fee = 0);

/// <param name="After">The holding after the trade; null when the position is fully closed.</param>
/// <param name="Warning">Set when the trade is valid but the cost could not be computed.</param>
public sealed record TradeResult(bool IsValid, Holding? After, string? Error = null, string? Warning = null)
{
    public static TradeResult Invalid(string error) => new(false, null, error);
}

public static class TradeCalculator
{
    public const int CostDecimals = 4;

    public static TradeResult Apply(Holding? before, Trade trade, CostMethod method)
    {
        if (before is not null && before.Id != trade.Id)
            throw new ArgumentException("持仓与交易的证券不一致", nameof(before));
        if (trade.Quantity <= 0) return TradeResult.Invalid("数量应为正整数");
        if (trade.Price <= 0) return TradeResult.Invalid("价格应大于 0");
        if (trade.Fee < 0) return TradeResult.Invalid("手续费不能为负数");

        return trade.Side == TradeSide.Buy ? Buy(before, trade) : Sell(before, trade, method);
    }

    private static TradeResult Buy(Holding? before, Trade t)
    {
        var amount = t.Quantity * t.Price + t.Fee;
        if (before is null)
            return new TradeResult(true, new Holding(t.Id, t.Quantity, Round(amount / t.Quantity)));

        var shares = before.Shares + t.Quantity;
        if (before.CostPrice is not { } cost)
            return new TradeResult(true, before with { Shares = shares, CostPrice = null },
                Warning: "原持仓没有成本价，只更新了股数。可在「编辑持仓」里补填成本价");

        return new TradeResult(true, before with { Shares = shares, CostPrice = Round((before.Shares * cost + amount) / shares) });
    }

    private static TradeResult Sell(Holding? before, Trade t, CostMethod method)
    {
        if (before is null) return TradeResult.Invalid("没有这只股票的持仓，不能卖出");
        if (t.Quantity > before.Shares) return TradeResult.Invalid($"卖出数量超过持仓（持有 {before.Shares:N0} 股）");

        var shares = before.Shares - t.Quantity;
        if (shares == 0) return new TradeResult(true, null);

        if (before.CostPrice is not { } cost)
            return new TradeResult(true, before with { Shares = shares },
                Warning: "原持仓没有成本价，只更新了股数");

        if (method == CostMethod.WeightedAverage)
            return new TradeResult(true, before with { Shares = shares });

        var proceeds = t.Quantity * t.Price - t.Fee;
        var diluted = Round((before.Shares * cost - proceeds) / shares);
        return new TradeResult(true, before with { Shares = shares, CostPrice = diluted },
            Warning: diluted <= 0 ? "已收回全部本金，摊薄成本为零或负数" : null);
    }

    private static decimal Round(decimal value) => Math.Round(value, CostDecimals, MidpointRounding.AwayFromZero);
}

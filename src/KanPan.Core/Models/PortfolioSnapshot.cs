namespace KanPan.Core.Models;

/// <summary>
/// Per-holding figures. Money amounts are in CNY (Hong Kong holdings converted at <paramref name="FxRate"/>);
/// prices stay in the quote currency. All *Pct values are percentages (1.28 means 1.28%).
/// </summary>
/// <param name="IsCurrent">Whether the quote belongs to the snapshot's session date.</param>
/// <param name="FxRate">CNY per unit of the quote currency; null when an HKD rate is not available yet,
/// in which case the amounts are zero and the holding is left out of the totals.</param>
public sealed record HoldingView(
    Holding Holding,
    Quote? Quote,
    decimal MarketValue,
    decimal DayPnl,
    decimal DayPct,
    decimal? TotalPnl,
    decimal? TotalPct,
    bool IsCurrent,
    decimal? FxRate = 1m);

/// <param name="SessionDate">The trading day the "day" figures refer to.</param>
/// <param name="Phase">The most active phase among the markets held.</param>
/// <param name="IsStale">The latest refresh failed; figures are from <paramref name="LastSuccessAt"/>.</param>
/// <param name="HkdCnyRate">The rate used for Hong Kong holdings, if any.</param>
public sealed record PortfolioSnapshot(
    IReadOnlyList<HoldingView> Items,
    decimal TotalMarketValue,
    decimal DayPnl,
    decimal DayPct,
    decimal? TotalPnl,
    decimal? TotalPct,
    int CostCoveredCount,
    DateOnly? SessionDate,
    MarketPhase Phase,
    DateTime UpdatedAt,
    DateTime? LastSuccessAt,
    bool IsStale,
    decimal? HkdCnyRate = null)
{
    public bool HasHongKong => Items.Any(i => i.Holding.Id.IsHongKong);

    public static PortfolioSnapshot Empty(DateTime now) =>
        new([], 0, 0, 0, null, null, 0, null, MarketPhase.Closed, now, null, false);
}

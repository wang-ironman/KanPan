using KanPan.Core.Models;
using KanPan.Core.Trades;

namespace KanPan.Core.Pnl;

public static class PnlCalculator
{
    /// <summary>
    /// "Day" figures refer to the latest session found in the quotes, so on weekends and before the open
    /// they show the previous session. A quote older than that session (e.g. a long suspension, or a market
    /// that is closed for a holiday) contributes no day P&L. Holdings without a quote are listed but excluded
    /// from totals, and so are Hong Kong holdings while <paramref name="hkdCny"/> is unknown.
    /// Recorded executions adjust day P&amp;L for actual buy/sell prices and fees, including closed positions.
    /// The day percentage is measured against opening shares valued at the previous close, excluding cash.
    /// Previous and current values use the same rate, so FX moves never show up as day P&L.
    /// </summary>
    public static PortfolioSnapshot Calculate(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<SecurityId, Quote> quotes,
        MarketPhase phase,
        DateTime updatedAt,
        DateTime? lastSuccessAt,
        bool isStale,
        decimal? hkdCny = null,
        IReadOnlyList<RecordedTrade>? trades = null)
    {
        trades ??= [];
        DateOnly? sessionDate = null;
        var latestTradeDate = trades.Count > 0 ? trades.Max(t => t.Date) : (DateOnly?)null;
        var ids = holdings.Select(h => h.Id)
            .Concat(trades.Where(t => t.Date == latestTradeDate).Select(t => t.Trade.Id)).Distinct();
        foreach (var id in ids)
        {
            if (quotes.TryGetValue(id, out var q))
            {
                var d = DateOnly.FromDateTime(q.QuoteTime);
                if (sessionDate is null || d > sessionDate) sessionDate = d;
            }
        }

        var items = new List<HoldingView>(holdings.Count);
        decimal marketValue = 0, baseValue = 0, dayPnl = 0;
        decimal totalPnl = 0, costValue = 0;
        var costCovered = 0;

        foreach (var h in holdings)
        {
            if (!quotes.TryGetValue(h.Id, out var q))
            {
                items.Add(new HoldingView(h, null, 0, 0, 0, null, null, false));
                continue;
            }

            var isCurrent = DateOnly.FromDateTime(q.QuoteTime) == sessionDate;
            var dpct = isCurrent ? q.ChangePct : 0;
            decimal? rate = h.Id.IsHongKong ? hkdCny : 1m;
            if (rate is not { } r)
            {
                items.Add(new HoldingView(h, q, 0, 0, dpct, null, null, isCurrent, FxRate: null));
                continue;
            }

            var mv = q.Price * h.Shares * r;
            var pv = isCurrent ? q.PrevClose * h.Shares * r : mv;
            var dp = mv - pv;

            decimal? tp = null, tpct = null;
            if (h.CostPrice is { } cost)
            {
                var cv = cost * h.Shares * r;
                tp = mv - cv;
                // A diluted cost at or below zero has no meaningful return percentage.
                tpct = cv > 0 ? tp / cv * 100m : null;
                totalPnl += tp.Value;
                costValue += cv;
                costCovered++;
            }

            marketValue += mv;
            baseValue += pv;
            dayPnl += dp;
            items.Add(new HoldingView(h, q, mv, dp, dpct, tp, tpct, isCurrent, r));
        }

        // Current shares already include today's executions. Restore the opening shares at yesterday's
        // close and include actual execution cash flows, including fees and fully closed positions.
        foreach (var group in trades.Where(t => t.Date == sessionDate).GroupBy(t => t.Trade.Id))
        {
            if (!quotes.TryGetValue(group.Key, out var q) || DateOnly.FromDateTime(q.QuoteTime) != sessionDate)
                continue;
            decimal? rate = group.Key.IsHongKong ? hkdCny : 1m;
            if (rate is not { } r) continue;

            decimal correction = 0, baseCorrection = 0;
            foreach (var entry in group)
            {
                var t = entry.Trade;
                var direction = t.Side == TradeSide.Buy ? 1m : -1m;
                correction += (direction * t.Quantity * (q.PrevClose - t.Price) - t.Fee) * r;
                baseCorrection -= direction * t.Quantity * q.PrevClose * r;
            }
            dayPnl += correction;
            baseValue += baseCorrection;
            var index = items.FindIndex(i => i.Holding.Id == group.Key);
            if (index >= 0) items[index] = items[index] with { DayPnl = items[index].DayPnl + correction };
        }

        return new PortfolioSnapshot(
            items,
            marketValue,
            dayPnl,
            baseValue == 0 ? 0 : dayPnl / baseValue * 100m,
            costCovered > 0 ? totalPnl : null,
            costValue > 0 ? totalPnl / costValue * 100m : null,
            costCovered,
            sessionDate,
            phase,
            updatedAt,
            lastSuccessAt,
            isStale,
            hkdCny);
    }
}

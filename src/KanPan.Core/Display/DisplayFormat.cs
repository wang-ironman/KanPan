using System.Globalization;
using KanPan.Core.Models;

namespace KanPan.Core.Display;

public static class DisplayFormat
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string SignedAmount(decimal value) =>
        (value > 0 ? "+" : "") + value.ToString("N2", Culture);

    public static string Amount(decimal value) => value.ToString("N2", Culture);

    public static string SignedPct(decimal pct) =>
        (pct > 0 ? "+" : "") + pct.ToString("0.00", Culture) + "%";

    /// <summary>Keeps the source precision (ETFs quote 3 decimals) with at least 2.</summary>
    public static string Price(decimal price) =>
        price.ToString("N" + Math.Clamp((int)price.Scale, 2, 3), Culture);

    /// <summary>
    /// Text drawn on the tray icon. Direction is conveyed by color, so no sign:
    /// one decimal below 10% ("1.3"), whole number up to 99 ("12"), then "99".
    /// Truncates rather than rounds so 4.99% never reads as "5.0".
    /// </summary>
    public static string TrayBadge(decimal pct)
    {
        var a = Math.Abs(pct);
        if (a < 10) return (Math.Truncate(a * 10) / 10).ToString("0.0", Culture);
        return Math.Min(99, (int)a).ToString(Culture);
    }

    /// <summary>"+3,330.00 (+8.64%)", or just the amount when there is no meaningful percentage.</summary>
    public static string PnlWithPct(decimal pnl, decimal? pct) =>
        pct is { } p ? $"{SignedAmount(pnl)} ({SignedPct(p)})" : SignedAmount(pnl);

    public static string PhaseText(MarketPhase phase) => phase switch
    {
        MarketPhase.PreOpen => "集合竞价",
        MarketPhase.Trading => "交易中",
        MarketPhase.Lunch => "午间休市",
        MarketPhase.ClosingAuction => "收市竞价",
        _ => "已收盘",
    };

    /// <summary>"当日盈亏", or "10-20 盈亏" when the figures belong to an earlier session.</summary>
    public static string DayLabel(DateOnly? sessionDate, DateOnly today) =>
        sessionDate is { } d && d != today ? $"{d:MM-dd} 盈亏" : "当日盈亏";

    /// <summary>Windows caps tray tooltips at 127 characters.</summary>
    public static string TrayTooltip(PortfolioSnapshot s, DateOnly today, bool alertsPaused)
    {
        var lines = new List<string> { $"KanPan  {s.UpdatedAt:HH:mm:ss}  {PhaseText(s.Phase)}" };
        if (s.Items.Count == 0 && s.SessionDate is null)
        {
            lines.Add("尚未导入持仓");
        }
        else
        {
            lines.Add($"{DayLabel(s.SessionDate, today)}：{SignedAmount(s.DayPnl)} ({SignedPct(s.DayPct)})");
            lines.Add($"总市值：{Amount(s.TotalMarketValue)}");
        }
        if (s.IsStale) lines.Add(s.LastSuccessAt is { } t ? $"网络异常，数据停留在 {t:HH:mm:ss}" : "网络异常，暂无数据");
        if (alertsPaused) lines.Add("今日提醒已暂停");

        var text = string.Join("\n", lines);
        return text.Length <= 127 ? text : text[..127];
    }
}

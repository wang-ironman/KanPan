using KanPan.Core.Models;

namespace KanPan.Core.Schedule;

/// <summary>
/// Trading-session tables (Beijing time, which Hong Kong shares). Holidays are not listed here; they are
/// detected at runtime from quote timestamps (see <see cref="QuoteScheduler"/>). Hong Kong half-day sessions
/// are not modelled either: the market simply stops updating and polling continues until the normal close.
/// </summary>
public static class MarketCalendar
{
    private static readonly TimeSpan MorningStart = new(9, 30, 0);
    private static readonly TimeSpan AfternoonStart = new(13, 0, 0);

    private static readonly TimeSpan APreOpenStart = new(9, 15, 0);
    private static readonly TimeSpan AMorningEnd = new(11, 30, 0);
    private static readonly TimeSpan AAfternoonEnd = new(15, 0, 0);

    private static readonly TimeSpan HkPreOpenStart = new(9, 0, 0);
    private static readonly TimeSpan HkMorningEnd = new(12, 0, 0);
    private static readonly TimeSpan HkAfternoonEnd = new(16, 0, 0);
    private static readonly TimeSpan HkAuctionEnd = new(16, 10, 0);

    public static MarketPhase GetPhase(Market market, DateTime beijingNow) => GetPhase(market.Group(), beijingNow);

    public static MarketPhase GetPhase(MarketGroup group, DateTime beijingNow)
    {
        if (beijingNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return MarketPhase.Closed;

        var t = beijingNow.TimeOfDay;
        var hk = group == MarketGroup.HongKong;
        if (t < (hk ? HkPreOpenStart : APreOpenStart)) return MarketPhase.Closed;
        if (t < MorningStart) return MarketPhase.PreOpen;
        if (t < (hk ? HkMorningEnd : AMorningEnd)) return MarketPhase.Trading;
        if (t < AfternoonStart) return MarketPhase.Lunch;
        if (t < (hk ? HkAfternoonEnd : AAfternoonEnd)) return MarketPhase.Trading;
        if (hk && t < HkAuctionEnd) return MarketPhase.ClosingAuction;
        return MarketPhase.Closed;
    }

    public static bool CanJudgeHoliday(Market market, DateTime beijingNow) => CanJudgeHoliday(market.Group(), beijingNow);

    /// <summary>Whether enough of the session has passed that a stale quote date means the market is on holiday.</summary>
    public static bool CanJudgeHoliday(MarketGroup group, DateTime beijingNow) =>
        GetPhase(group, beijingNow) is MarketPhase.Trading or MarketPhase.Lunch
        && beijingNow.TimeOfDay >= MorningStart + TimeSpan.FromMinutes(10);
}

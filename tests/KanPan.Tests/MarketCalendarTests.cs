using KanPan.Core.Models;
using KanPan.Core.Schedule;

namespace KanPan.Tests;

public class MarketCalendarTests
{
    [Theory]
    [InlineData("09:14:59", MarketPhase.Closed)]
    [InlineData("09:15:00", MarketPhase.PreOpen)]
    [InlineData("09:29:59", MarketPhase.PreOpen)]
    [InlineData("09:30:00", MarketPhase.Trading)]
    [InlineData("11:29:59", MarketPhase.Trading)]
    [InlineData("11:30:00", MarketPhase.Lunch)]
    [InlineData("12:59:59", MarketPhase.Lunch)]
    [InlineData("13:00:00", MarketPhase.Trading)]
    [InlineData("14:59:59", MarketPhase.Trading)]
    [InlineData("15:00:00", MarketPhase.Closed)]
    public void Weekday_boundaries(string time, MarketPhase expected)
    {
        var wednesday = new DateOnly(2026, 10, 21).ToDateTime(TimeOnly.Parse(time));
        Assert.Equal(expected, MarketCalendar.GetPhase(Market.SH, wednesday));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    public void Weekend_is_closed(int day) =>
        Assert.Equal(MarketPhase.Closed, MarketCalendar.GetPhase(Market.SZ, new DateTime(2026, 10, day, 10, 0, 0)));

    [Theory]
    [InlineData("09:35:00", false)]
    [InlineData("09:40:00", true)]
    [InlineData("12:00:00", true)]
    [InlineData("15:30:00", false)]
    public void Holiday_judgement_waits_for_session(string time, bool expected) =>
        Assert.Equal(expected, MarketCalendar.CanJudgeHoliday(Market.SH, new DateOnly(2026, 10, 21).ToDateTime(TimeOnly.Parse(time))));

    [Theory]
    [InlineData("08:59:59", MarketPhase.Closed)]
    [InlineData("09:00:00", MarketPhase.PreOpen)]
    [InlineData("09:30:00", MarketPhase.Trading)]
    [InlineData("11:45:00", MarketPhase.Trading)]
    [InlineData("12:00:00", MarketPhase.Lunch)]
    [InlineData("13:00:00", MarketPhase.Trading)]
    [InlineData("15:30:00", MarketPhase.Trading)]
    [InlineData("16:00:00", MarketPhase.ClosingAuction)]
    [InlineData("16:09:59", MarketPhase.ClosingAuction)]
    [InlineData("16:10:00", MarketPhase.Closed)]
    public void Hong_Kong_boundaries(string time, MarketPhase expected)
    {
        var wednesday = new DateOnly(2026, 10, 21).ToDateTime(TimeOnly.Parse(time));
        Assert.Equal(expected, MarketCalendar.GetPhase(Market.HK, wednesday));
    }

    [Fact]
    public void Hong_Kong_weekend_is_closed() =>
        Assert.Equal(MarketPhase.Closed, MarketCalendar.GetPhase(Market.HK, new DateTime(2026, 10, 24, 10, 0, 0)));

    [Theory]
    [InlineData("12:30:00", true)]
    [InlineData("15:30:00", true)]
    [InlineData("16:05:00", false)]
    public void Hong_Kong_holiday_judgement(string time, bool expected) =>
        Assert.Equal(expected, MarketCalendar.CanJudgeHoliday(Market.HK, new DateOnly(2026, 10, 21).ToDateTime(TimeOnly.Parse(time))));

    [Fact]
    public void Beijing_time_is_utc_plus_eight()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 21, 1, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateTime(2026, 10, 21, 9, 30, 0), BeijingTime.Now(time));
    }
}

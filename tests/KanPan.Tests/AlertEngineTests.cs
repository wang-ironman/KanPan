using KanPan.Core.Alerts;
using KanPan.Core.Models;

namespace KanPan.Tests;

public class AlertEngineTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 21);
    private static readonly SecurityId A = SecurityId.Parse("600519");
    private static readonly SecurityId B = SecurityId.Parse("000001");

    private static Quote Q(SecurityId id, decimal pct) =>
        new(id, id.Code, 100m + pct, 100m, Day1.ToDateTime(new TimeOnly(10, 0)));

    private static AlertEngine Engine(bool realert = false) => new([5m, 7m, 9.5m], realert);

    [Fact]
    public void Below_threshold_does_not_alert() =>
        Assert.Empty(Engine().Evaluate([Q(A, 4.99m), Q(B, -4.99m)], Day1));

    [Fact]
    public void Crossing_threshold_alerts_once()
    {
        var engine = Engine();
        var e = Assert.Single(engine.Evaluate([Q(A, 5.1m)], Day1));
        Assert.True(e.IsUp);
        Assert.Equal(1, e.Level);
        Assert.Equal(5m, e.Threshold);

        Assert.Empty(engine.Evaluate([Q(A, 5.5m)], Day1));
        Assert.Empty(engine.Evaluate([Q(A, 4.0m)], Day1));
        Assert.Empty(engine.Evaluate([Q(A, 6.9m)], Day1));
    }

    [Fact]
    public void Each_higher_level_alerts()
    {
        var engine = Engine();
        Assert.Single(engine.Evaluate([Q(A, 5m)], Day1));
        Assert.Equal(2, Assert.Single(engine.Evaluate([Q(A, 7m)], Day1)).Level);
        Assert.Equal(3, Assert.Single(engine.Evaluate([Q(A, 10m)], Day1)).Level);
        Assert.Empty(engine.Evaluate([Q(A, 10m)], Day1));
    }

    [Fact]
    public void Jumping_several_levels_alerts_once_at_highest()
    {
        var e = Assert.Single(Engine().Evaluate([Q(A, 8m)], Day1));
        Assert.Equal(2, e.Level);
    }

    [Fact]
    public void Directions_are_tracked_separately()
    {
        var engine = Engine();
        Assert.Single(engine.Evaluate([Q(A, 6m)], Day1));
        var down = Assert.Single(engine.Evaluate([Q(A, -6m)], Day1));
        Assert.False(down.IsUp);
    }

    [Fact]
    public void New_trade_date_resets_state()
    {
        var engine = Engine();
        Assert.Single(engine.Evaluate([Q(A, 6m)], Day1));
        Assert.Single(engine.Evaluate([Q(A, 6m)], Day1.AddDays(1)));
    }

    [Fact]
    public void State_survives_restart()
    {
        var first = Engine();
        first.Evaluate([Q(A, 6m)], Day1);

        var restarted = new AlertEngine([5m, 7m, 9.5m], false, first.State);
        Assert.Empty(restarted.Evaluate([Q(A, 6m)], Day1));
    }

    [Fact]
    public void Realert_requires_falling_below_buffer()
    {
        var engine = Engine(realert: true);
        Assert.Single(engine.Evaluate([Q(A, 5.2m)], Day1));
        Assert.Empty(engine.Evaluate([Q(A, 4.2m)], Day1));
        Assert.Empty(engine.Evaluate([Q(A, 5.2m)], Day1));
        Assert.Empty(engine.Evaluate([Q(A, 3.9m)], Day1));
        Assert.Single(engine.Evaluate([Q(A, 5.2m)], Day1));
    }

    [Fact]
    public void Without_realert_a_retreat_never_rearms()
    {
        var engine = Engine();
        Assert.Single(engine.Evaluate([Q(A, 5.2m)], Day1));
        Assert.Empty(engine.Evaluate([Q(A, 0m)], Day1));
        Assert.Empty(engine.Evaluate([Q(A, 5.2m)], Day1));
    }

    [Fact]
    public void Suspended_quotes_are_ignored()
    {
        var suspended = Q(A, 6m) with { IsSuspended = true };
        Assert.Empty(Engine().Evaluate([suspended], Day1));
    }

    [Fact]
    public void Requires_a_threshold() =>
        Assert.Throws<ArgumentException>(() => new AlertEngine([], false));
}

public class AlertMessagesTests
{
    private static AlertEvent E(string code, decimal pct) =>
        new(SecurityId.Parse(code), "股票" + code, pct, 10m, 1, 5m);

    [Fact]
    public void Single_event_has_direction_tag_and_symbol()
    {
        var m = Assert.Single(AlertMessages.Compose([E("600519", -5.32m)], isFirstRound: false, 5m));
        Assert.Equal("【大跌】股票600519 -5.32%", m.Title);
        Assert.Equal("sh600519", m.Symbol);
    }

    [Fact]
    public void Up_to_three_events_are_listed_individually() =>
        Assert.Equal(3, AlertMessages.Compose([E("600519", 5), E("000001", 6), E("300750", -7)], false, 5m).Count);

    [Fact]
    public void More_than_three_events_are_merged()
    {
        var events = new[] { E("600519", 5), E("000001", 6), E("300750", -7), E("510300", 5.5m) };
        var m = Assert.Single(AlertMessages.Compose(events, false, 5m));
        Assert.Equal("4 只持仓涨跌幅超过 5%", m.Title);
        Assert.StartsWith("股票300750 -7.00%", m.Body);
        Assert.Null(m.Symbol);
    }

    [Fact]
    public void First_round_merges_even_two_events() =>
        Assert.Single(AlertMessages.Compose([E("600519", 5), E("000001", 6)], isFirstRound: true, 5m));

    [Fact]
    public void First_round_single_event_stays_single() =>
        Assert.NotNull(Assert.Single(AlertMessages.Compose([E("600519", 5)], isFirstRound: true, 5m)).Symbol);
}

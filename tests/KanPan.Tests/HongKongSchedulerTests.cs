using KanPan.Core.Alerts;
using KanPan.Core.Models;
using KanPan.Core.Schedule;

namespace KanPan.Tests;

/// <summary>Scheduling with both A-share and Hong Kong holdings, which keep separate sessions.</summary>
public class HongKongSchedulerTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 21);
    private static readonly SecurityId A = SecurityId.Parse("600519");
    private static readonly SecurityId Hk = SecurityId.Parse("00700");

    private readonly FakeQuoteProvider _provider = new();
    private readonly FakeFxProvider _fx = new(0.85m);
    private readonly FakeTime _time = FakeTime.AtBeijing(Wednesday.ToDateTime(new TimeOnly(10, 0)));
    private readonly QuoteScheduler _sut;
    private readonly List<AlertEvent> _alerts = [];
    private readonly List<decimal> _rates = [];
    private readonly Dictionary<SecurityId, DateTime> _quoteTimes = new();

    private decimal _pct;

    public HongKongSchedulerTests()
    {
        _provider.Respond = ids => ids
            .Select(id => new Quote(id, id.Symbol, 100m + _pct, 100m, _quoteTimes.GetValueOrDefault(id, BeijingTime.Now(_time))))
            .ToList();
        _sut = new QuoteScheduler(_provider, new AlertEngine([5m], false), _time, fx: _fx);
        _sut.AlertsRaised += (e, _) => _alerts.AddRange(e);
        _sut.HkdCnyRateUpdated += _rates.Add;
        _sut.SetHoldings([new Holding(A, 100, null), new Holding(Hk, 100, null)]);
    }

    private void At(int hour, int minute) => _time.SetBeijing(Wednesday.ToDateTime(new TimeOnly(hour, minute)));

    [Fact]
    public async Task After_a_share_close_only_hong_kong_is_polled()
    {
        At(15, 30);
        await _sut.RunOnceAsync(default);
        Assert.Equal(TimeSpan.FromSeconds(5), await _sut.RunOnceAsync(default));

        Assert.Equal([Hk], _provider.Requests[^1]);
        Assert.Equal(MarketPhase.Trading, _sut.Latest.Phase);
    }

    [Fact]
    public async Task Closing_auction_polls_without_alerts_then_fetches_final_price()
    {
        _pct = 6m;
        At(16, 5);
        await _sut.RunOnceAsync(default);
        Assert.Equal(MarketPhase.ClosingAuction, _sut.Latest.Phase);
        Assert.Empty(_alerts);

        At(16, 11);
        await _sut.RunOnceAsync(default);
        Assert.Equal([Hk], _provider.Requests[^1]);
        var calls = _provider.Calls;
        Assert.Equal(QuoteScheduler.ClosedRecheckInterval, await _sut.RunOnceAsync(default));
        Assert.Equal(calls, _provider.Calls);
        Assert.Equal(MarketPhase.Closed, _sut.Latest.Phase);
    }

    [Fact]
    public async Task Both_at_lunch_then_both_alert_in_the_afternoon()
    {
        _pct = 6m;
        At(12, 30);
        await _sut.RunOnceAsync(default);
        At(13, 30);
        await _sut.RunOnceAsync(default);
        Assert.Equal(2, _alerts.Count);
    }

    [Fact]
    public async Task Alerts_only_for_markets_in_continuous_trading()
    {
        _pct = 6m;
        At(11, 45); // A shares at lunch, Hong Kong trading
        await _sut.RunOnceAsync(default);
        Assert.Equal(Hk, Assert.Single(_alerts).Id);
    }

    [Fact]
    public async Task Hong_kong_holiday_does_not_stop_a_shares()
    {
        _quoteTimes[Hk] = Wednesday.AddDays(-1).ToDateTime(new TimeOnly(16, 8));
        _pct = 6m;
        await _sut.RunOnceAsync(default);

        Assert.Equal(A, Assert.Single(_alerts).Id);
        Assert.Equal(MarketPhase.Trading, _sut.Latest.Phase);
        await _sut.RunOnceAsync(default);
        Assert.Equal([A], _provider.Requests[^1]);
        Assert.Equal(0m, _sut.Latest.Items.Single(i => i.Holding.Id == Hk).DayPnl);
    }

    [Fact]
    public async Task Rate_is_fetched_at_start_then_every_ten_minutes()
    {
        await _sut.RunOnceAsync(default);
        Assert.Equal([0.85m], _rates);
        Assert.Equal(0.85m, _sut.Latest.HkdCnyRate);
        Assert.Equal(100m * 100 * 0.85m, _sut.Latest.Items.Single(i => i.Holding.Id == Hk).MarketValue);

        _time.Advance(TimeSpan.FromMinutes(5));
        await _sut.RunOnceAsync(default);
        Assert.Equal(1, _fx.Calls);

        _fx.Rate = 0.86m;
        _time.Advance(TimeSpan.FromMinutes(5));
        await _sut.RunOnceAsync(default);
        Assert.Equal(2, _fx.Calls);
        Assert.Equal([0.85m, 0.86m], _rates);
    }

    [Fact]
    public async Task Rate_failure_keeps_cached_rate_and_quotes_fresh()
    {
        _fx.FailWith = new HttpRequestException("down");
        var sut = new QuoteScheduler(_provider, new AlertEngine([5m], false), _time, fx: _fx) { HkdCnyRate = 0.9m };
        sut.SetHoldings([new Holding(Hk, 100, null)]);

        await sut.RunOnceAsync(default);
        Assert.Equal(0.9m, sut.Latest.HkdCnyRate);
        Assert.False(sut.Latest.IsStale);

        _time.Advance(TimeSpan.FromSeconds(30));
        await sut.RunOnceAsync(default);
        Assert.Equal(1, _fx.Calls);
        _time.Advance(QuoteScheduler.FxRetryInterval);
        await sut.RunOnceAsync(default);
        Assert.Equal(2, _fx.Calls);
    }

    [Fact]
    public async Task No_rate_is_fetched_without_hong_kong_holdings()
    {
        _sut.SetHoldings([new Holding(A, 100, null)]);
        await _sut.RunOnceAsync(default);
        Assert.Equal(0, _fx.Calls);
    }

    [Fact]
    public async Task Before_a_shares_open_hong_kong_pre_open_is_polled()
    {
        At(9, 5);
        await _sut.RunOnceAsync(default);
        await _sut.RunOnceAsync(default);
        Assert.Equal([Hk], _provider.Requests[^1]);
        Assert.Equal(MarketPhase.PreOpen, _sut.Latest.Phase);
    }
}

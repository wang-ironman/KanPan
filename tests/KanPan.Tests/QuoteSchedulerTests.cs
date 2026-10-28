using KanPan.Core.Alerts;
using KanPan.Core.Models;
using KanPan.Core.Schedule;

namespace KanPan.Tests;

public class QuoteSchedulerTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 21);
    private static readonly SecurityId A = SecurityId.Parse("600519");

    private readonly FakeQuoteProvider _provider = new();
    private readonly FakeTime _time = FakeTime.AtBeijing(Wednesday.ToDateTime(new TimeOnly(10, 0)));
    private readonly QuoteScheduler _sut;
    private readonly List<(IReadOnlyList<AlertEvent> Events, bool First)> _alerts = [];

    private decimal _pct;
    private DateTime? _quoteTime;

    public QuoteSchedulerTests()
    {
        _provider.Respond = ids => ids
            .Select(id => new Quote(id, "X", 100m + _pct, 100m, _quoteTime ?? BeijingTime.Now(_time)))
            .ToList();
        _sut = new QuoteScheduler(_provider, new AlertEngine([5m, 7m], false), _time);
        _sut.AlertsRaised += (e, first) => _alerts.Add((e, first));
        _sut.SetHoldings([new Holding(A, 100, null)]);
    }

    private void At(int hour, int minute) => _time.SetBeijing(Wednesday.ToDateTime(new TimeOnly(hour, minute)));

    [Fact]
    public async Task Trading_polls_at_refresh_interval()
    {
        _sut.RefreshInterval = TimeSpan.FromSeconds(3);
        Assert.Equal(TimeSpan.FromSeconds(3), await _sut.RunOnceAsync(default));
        await _sut.RunOnceAsync(default);
        Assert.Equal(2, _provider.Calls);
        Assert.Equal(MarketPhase.Trading, _sut.Latest.Phase);
    }

    [Fact]
    public async Task Lunch_polls_slowly()
    {
        At(12, 0);
        Assert.Equal(QuoteScheduler.LunchInterval, await _sut.RunOnceAsync(default));
    }

    [Fact]
    public async Task Closed_fetches_once_then_stops()
    {
        At(20, 0);
        await _sut.RunOnceAsync(default);
        Assert.Equal(QuoteScheduler.ClosedRecheckInterval, await _sut.RunOnceAsync(default));
        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task Fetches_final_quote_when_session_closes()
    {
        At(14, 59);
        await _sut.RunOnceAsync(default);
        At(15, 1);
        await _sut.RunOnceAsync(default);
        await _sut.RunOnceAsync(default);
        Assert.Equal(2, _provider.Calls);
    }

    [Fact]
    public async Task RefreshNow_forces_fetch_while_closed()
    {
        At(20, 0);
        await _sut.RunOnceAsync(default);
        _sut.RefreshNow();
        await _sut.RunOnceAsync(default);
        Assert.Equal(2, _provider.Calls);
    }

    [Fact]
    public async Task Alerts_only_during_continuous_trading()
    {
        _pct = 6m;
        At(9, 20);
        await _sut.RunOnceAsync(default);
        Assert.Empty(_alerts);

        At(9, 31);
        await _sut.RunOnceAsync(default);
        var (events, first) = Assert.Single(_alerts);
        Assert.Single(events);
        Assert.True(first);

        _pct = 7.5m;
        await _sut.RunOnceAsync(default);
        Assert.False(_alerts[1].First);
    }

    [Fact]
    public async Task Paused_day_skips_alerts()
    {
        _pct = 6m;
        _sut.AlertsPausedOn = Wednesday;
        await _sut.RunOnceAsync(default);
        Assert.Empty(_alerts);
    }

    [Fact]
    public async Task Stale_quotes_after_open_mean_holiday()
    {
        _quoteTime = Wednesday.AddDays(-1).ToDateTime(new TimeOnly(15, 0));
        _pct = 6m;
        await _sut.RunOnceAsync(default);

        Assert.Equal(MarketPhase.Closed, _sut.Latest.Phase);
        Assert.Empty(_alerts);
        await _sut.RunOnceAsync(default);
        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task Failure_backs_off_and_marks_stale()
    {
        await _sut.RunOnceAsync(default);
        _provider.FailWith = new HttpRequestException("down");

        Assert.Equal(TimeSpan.FromSeconds(5), await _sut.RunOnceAsync(default));
        Assert.True(_sut.Latest.IsStale);
        Assert.Single(_sut.Latest.Items, i => i.Quote is not null);
        Assert.Equal(TimeSpan.FromSeconds(10), await _sut.RunOnceAsync(default));
        Assert.Equal(TimeSpan.FromSeconds(30), await _sut.RunOnceAsync(default));
        Assert.Equal(TimeSpan.FromSeconds(60), await _sut.RunOnceAsync(default));
        Assert.Equal(TimeSpan.FromSeconds(60), await _sut.RunOnceAsync(default));

        _provider.FailWith = null;
        await _sut.RunOnceAsync(default);
        Assert.False(_sut.Latest.IsStale);
    }

    [Fact]
    public async Task No_holdings_no_fetch()
    {
        _sut.SetHoldings([]);
        await _sut.RunOnceAsync(default);
        Assert.Equal(0, _provider.Calls);
    }
}

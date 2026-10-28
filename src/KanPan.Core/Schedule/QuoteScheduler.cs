using KanPan.Core.Alerts;
using KanPan.Core.Models;
using KanPan.Core.Pnl;
using KanPan.Core.Quotes;
using KanPan.Core.Trades;

namespace KanPan.Core.Schedule;

/// <summary>
/// Polls quotes according to each market's session, recomputes the portfolio snapshot and runs alert
/// detection. A-shares and Hong Kong keep separate session and holiday state, and each round only requests
/// the markets that are open (plus anything forced or missing). Events are raised on a background thread.
/// </summary>
public sealed class QuoteScheduler : IAsyncDisposable
{
    public static readonly TimeSpan LunchInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ClosedRecheckInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan FxRefreshInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan FxRetryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    private readonly IQuoteProvider _provider;
    private readonly IFxRateProvider? _fx;
    private readonly AlertEngine _alerts;
    private readonly TimeProvider _time;
    private readonly IAppLog _log;
    private readonly Dictionary<SecurityId, Quote> _quotes = new();
    private readonly Dictionary<MarketGroup, MarketPhase> _lastPhases = new();
    private readonly Dictionary<MarketGroup, DateOnly> _holidays = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    private sealed record PortfolioInput(IReadOnlyList<Holding> Holdings, IReadOnlyList<RecordedTrade> Trades);
    private volatile PortfolioInput _portfolio = new([], []);
    private volatile bool _forceRefresh = true;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private DateTime? _lastSuccessAt;
    private int _failures;
    private int _alertRounds;
    private decimal? _hkdCny;
    private DateTime? _nextFxAt;

    public QuoteScheduler(IQuoteProvider provider, AlertEngine alerts, TimeProvider time, IAppLog? log = null, IFxRateProvider? fx = null)
    {
        _provider = provider;
        _alerts = alerts;
        _time = time;
        _log = log ?? NullLog.Instance;
        _fx = fx;
        Latest = PortfolioSnapshot.Empty(BeijingTime.Now(time));
    }

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Alerts are skipped on this date (the "pause alerts for today" switch).</summary>
    public DateOnly? AlertsPausedOn { get; set; }

    /// <summary>
    /// CNY per HKD. Seed it with the last cached rate so Hong Kong holdings count from the first round;
    /// a fresh rate is still fetched at startup.
    /// </summary>
    public decimal? HkdCnyRate
    {
        get => _hkdCny;
        init => _hkdCny = value is > 0 ? value : null;
    }

    public PortfolioSnapshot Latest { get; private set; }

    public event Action<PortfolioSnapshot>? SnapshotUpdated;

    /// <summary>Second argument is true for the first alert round after launch.</summary>
    public event Action<IReadOnlyList<AlertEvent>, bool>? AlertsRaised;

    public event Action<AlertState>? AlertStateChanged;

    public event Action<decimal>? HkdCnyRateUpdated;

    public void SetHoldings(IReadOnlyList<Holding> holdings, IReadOnlyList<RecordedTrade>? trades = null)
    {
        _portfolio = new(holdings, trades ?? []);
        RefreshNow();
    }

    public void RefreshNow()
    {
        _forceRefresh = true;
        try { _wake.Release(); } catch (SemaphoreFullException) { }
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null || _loop is null) return;
        await _cts.CancelAsync();
        try { await _loop; } catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await RunOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Error("调度循环异常", ex);
                delay = Backoff[^1];
            }

            try { await _wake.WaitAsync(delay, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Runs one refresh round and returns how long to wait before the next one.</summary>
    public async Task<TimeSpan> RunOnceAsync(CancellationToken ct)
    {
        var now = BeijingTime.Now(_time);
        var today = DateOnly.FromDateTime(now);
        var portfolio = _portfolio;
        var holdings = portfolio.Holdings;
        var trades = portfolio.Trades;
        var force = _forceRefresh;
        _forceRefresh = false;

        // Closed positions still need a quote to value today's realized result and advance its date.
        var latestTradeDate = trades.Count > 0 ? trades.Max(t => t.Date) : (DateOnly?)null;
        var ids = holdings.Select(h => h.Id)
            .Concat(trades.Where(t => t.Date == latestTradeDate).Select(t => t.Trade.Id)).Distinct().ToList();
        var held = ids.Select(id => id.Group).Distinct().ToList();
        var phases = (held.Count > 0 ? held : [MarketGroup.AShare]).ToDictionary(g => g, g => PhaseOf(g, now, today));

        var toFetch = ids
            .Where(id => force || !_quotes.ContainsKey(id) || NeedsQuotes(id.Group, phases[id.Group]))
            .ToList();

        var fetchFailed = false;
        if (toFetch.Count > 0)
        {
            try
            {
                var quotes = await _provider.GetQuotesAsync(toFetch, ct);
                foreach (var q in quotes) _quotes[q.Id] = q;
                _failures = 0;
                _lastSuccessAt = now;

                foreach (var g in held)
                {
                    if (phases[g] == MarketPhase.Closed || !IsHoliday(g, quotes, today, now)) continue;
                    _log.Info($"{today} {(g == MarketGroup.HongKong ? "港股" : "A 股")}行情日期均非今日，判定为休市");
                    _holidays[g] = today;
                    phases[g] = MarketPhase.Closed;
                }

                if (phases.Values.Contains(MarketPhase.Trading) && AlertsPausedOn != today)
                    RunAlerts(holdings, phases, today);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _failures++;
                fetchFailed = true;
                _log.Error($"行情获取失败（第 {_failures} 次，数据源 {_provider.Name}）", ex);
            }
        }

        if (phases.TryGetValue(MarketGroup.HongKong, out var hkPhase))
            await RefreshFxAsync(hkPhase, force, now, ct);

        foreach (var (g, p) in phases) _lastPhases[g] = p;
        Latest = PnlCalculator.Calculate(
            holdings, _quotes, MostActive(phases.Values), now, _lastSuccessAt,
            isStale: fetchFailed || (_failures > 0 && toFetch.Count == 0),
            _hkdCny, trades);
        SnapshotUpdated?.Invoke(Latest);

        if (_failures > 0)
            return Backoff[Math.Min(_failures - 1, Backoff.Length - 1)];
        return phases.Values.Min(IntervalFor);
    }

    private MarketPhase PhaseOf(MarketGroup group, DateTime now, DateOnly today) =>
        _holidays.GetValueOrDefault(group) == today ? MarketPhase.Closed : MarketCalendar.GetPhase(group, now);

    /// <summary>Open markets are polled; a market that just closed gets one more round for the final price.</summary>
    private bool NeedsQuotes(MarketGroup group, MarketPhase phase) =>
        phase != MarketPhase.Closed || _lastPhases.GetValueOrDefault(group, MarketPhase.Closed) != MarketPhase.Closed;

    private TimeSpan IntervalFor(MarketPhase phase) => phase switch
    {
        MarketPhase.Trading or MarketPhase.PreOpen or MarketPhase.ClosingAuction => RefreshInterval,
        MarketPhase.Lunch => LunchInterval,
        _ => ClosedRecheckInterval,
    };

    private static MarketPhase MostActive(IEnumerable<MarketPhase> phases) =>
        phases.MaxBy(p => p switch
        {
            MarketPhase.Trading => 4,
            MarketPhase.ClosingAuction => 3,
            MarketPhase.PreOpen => 2,
            MarketPhase.Lunch => 1,
            _ => 0,
        });

    /// <summary>
    /// Fetched at startup, then every <see cref="FxRefreshInterval"/> while Hong Kong is open. A failure keeps
    /// the previous rate and retries after <see cref="FxRetryInterval"/>; it never marks the quotes as stale.
    /// </summary>
    private async Task RefreshFxAsync(MarketPhase hkPhase, bool force, DateTime now, CancellationToken ct)
    {
        if (_fx is null) return;
        var due = _nextFxAt is null || (now >= _nextFxAt && (hkPhase != MarketPhase.Closed || force || _hkdCny is null));
        if (!due) return;

        try
        {
            var rate = await _fx.GetHkdCnyAsync(ct);
            _nextFxAt = now + FxRefreshInterval;
            if (rate == _hkdCny) return;
            _hkdCny = rate;
            HkdCnyRateUpdated?.Invoke(rate);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _nextFxAt = now + FxRetryInterval;
            _log.Error($"港币汇率获取失败（数据源 {_fx.Name}），{(_hkdCny is { } r ? $"继续使用 {r}" : "港股暂不计入合计")}", ex);
        }
    }

    private void RunAlerts(IReadOnlyList<Holding> holdings, IReadOnlyDictionary<MarketGroup, MarketPhase> phases, DateOnly today)
    {
        var current = holdings
            .Where(h => phases[h.Id.Group] == MarketPhase.Trading)
            .Select(h => _quotes.GetValueOrDefault(h.Id))
            .Where(q => q is not null && DateOnly.FromDateTime(q.QuoteTime) == today)
            .Cast<Quote>()
            .ToList();

        var previousDate = _alerts.State.TradeDate;
        var events = _alerts.Evaluate(current, today);
        var isFirstRound = _alertRounds++ == 0;

        if (events.Count > 0)
            AlertsRaised?.Invoke(events, isFirstRound);
        if (events.Count > 0 || previousDate != today)
            AlertStateChanged?.Invoke(_alerts.State);
    }

    private static bool IsHoliday(MarketGroup group, IReadOnlyList<Quote> quotes, DateOnly today, DateTime now)
    {
        var mine = quotes.Where(q => q.Id.Group == group).ToList();
        return mine.Count > 0
            && MarketCalendar.CanJudgeHoliday(group, now)
            && mine.All(q => DateOnly.FromDateTime(q.QuoteTime) < today);
    }
}

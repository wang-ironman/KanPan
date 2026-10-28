using KanPan.Core.Models;
using KanPan.Core.Pnl;
using KanPan.Core.Alerts;
using KanPan.Core.Display;
using KanPan.Core.Schedule;
using KanPan.Core.Trades;
using KanPan.Infrastructure.Storage;

namespace KanPan.Tests;

public class AccountDayPnlTests
{
    private static readonly DateTime Today = new(2026, 10, 28, 14, 30, 0);
    private static readonly DateOnly TradeDate = DateOnly.FromDateTime(Today);
    private static readonly SecurityId A = SecurityId.Parse("600519");
    private static readonly SecurityId Hk = SecurityId.Parse("00700");

    private static PortfolioSnapshot Calc(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<RecordedTrade> trades,
        params Quote[] quotes) =>
        PnlCalculator.Calculate(
            holdings,
            quotes.ToDictionary(q => q.Id),
            MarketPhase.Trading,
            Today,
            Today,
            false,
            hkdCny: 0.85m,
            trades: trades);

    private static RecordedTrade Record(TradeSide side, long quantity, decimal price, decimal fee = 0,
        DateOnly? date = null, SecurityId? id = null) =>
        new(date ?? TradeDate, new Trade(id ?? A, side, quantity, price, fee));

    private static Quote QuoteFor(SecurityId id, decimal price, decimal previousClose, DateTime? time = null) =>
        new(id, id.Symbol, price, previousClose, time ?? Today);

    [Fact]
    public void Same_day_buy_counts_purchase_cost_and_fee_instead_of_all_current_shares()
    {
        var snapshot = Calc(
            [new(A, 100, 110m)],
            [Record(TradeSide.Buy, 100, 110m, 5m)],
            QuoteFor(A, 120m, 100m));

        Assert.Equal(995m, snapshot.DayPnl);
        Assert.Equal(995m, snapshot.Items.Single().DayPnl);
    }

    [Fact]
    public void Partial_sale_counts_realized_profit_and_remaining_position_change()
    {
        var snapshot = Calc(
            [new(A, 60, 90m)],
            [Record(TradeSide.Sell, 40, 110m, 5m)],
            QuoteFor(A, 120m, 100m));

        Assert.Equal(1595m, snapshot.DayPnl);
        Assert.Equal(1595m, snapshot.Items.Single().DayPnl);
        Assert.Equal(15.95m, snapshot.DayPct);
        Assert.Equal(20m, snapshot.Items.Single().DayPct);
    }

    [Fact]
    public void Full_sale_keeps_realized_profit_in_account_total_without_holding_row()
    {
        var snapshot = Calc(
            [],
            [Record(TradeSide.Sell, 100, 110m, 5m)],
            QuoteFor(A, 110m, 100m));

        Assert.Equal(995m, snapshot.DayPnl);
        Assert.Empty(snapshot.Items);
        Assert.Equal(9.95m, snapshot.DayPct);
    }

    [Fact]
    public void Buy_then_sell_same_day_counts_net_realized_profit_and_fees()
    {
        var snapshot = Calc(
            [],
            [Record(TradeSide.Buy, 100, 110m, 5m), Record(TradeSide.Sell, 100, 120m, 5m)],
            QuoteFor(A, 120m, 100m));

        Assert.Equal(990m, snapshot.DayPnl);
        Assert.Equal(0m, snapshot.DayPct);
    }

    [Fact]
    public void Previous_day_trades_do_not_affect_today_pnl()
    {
        var snapshot = Calc(
            [new(A, 100, 110m)],
            [Record(TradeSide.Buy, 100, 110m, 5m, TradeDate.AddDays(-1))],
            QuoteFor(A, 210m, 200m));

        Assert.Equal(1000m, snapshot.DayPnl);
    }

    [Fact]
    public void Hong_kong_trade_fees_and_realized_pnl_are_converted_to_cny()
    {
        var snapshot = Calc(
            [new(Hk, 60, 90m)],
            [Record(TradeSide.Sell, 40, 110m, 5m, id: Hk)],
            QuoteFor(Hk, 120m, 100m));

        Assert.Equal(1595m * 0.85m, snapshot.DayPnl);
        Assert.Equal(1595m * 0.85m, snapshot.Items.Single().DayPnl);
    }

    [Fact]
    public void Same_day_buy_is_accounted_when_holding_has_no_cost_price()
    {
        var snapshot = Calc(
            [new(A, 100, null)],
            [Record(TradeSide.Buy, 100, 110m, 5m)],
            QuoteFor(A, 120m, 100m));

        Assert.Equal(995m, snapshot.DayPnl);
    }

    [Fact]
    public void Account_day_pnl_sums_trade_adjusted_pnl_across_holdings()
    {
        var b = SecurityId.Parse("000001");
        var snapshot = Calc(
            [new(A, 60, null), new(b, 100, null)],
            [Record(TradeSide.Sell, 40, 110m, 5m)],
            QuoteFor(A, 120m, 100m),
            QuoteFor(b, 105m, 100m));

        Assert.Equal(2095m, snapshot.DayPnl);
        Assert.Equal(1595m, snapshot.Items.Single(i => i.Holding.Id == A).DayPnl);
        Assert.Equal(500m, snapshot.Items.Single(i => i.Holding.Id == b).DayPnl);
        Assert.Equal(2095m / 20000m * 100m, snapshot.DayPct);
    }

    [Fact]
    public void Portfolio_file_round_trip_preserves_realized_pnl_after_clearing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kanpan-account-pnl-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStore(dir);
            var sale = Record(TradeSide.Sell, 100, 110m, 5m);
            store.Save(PortfolioFile.FileName, PortfolioFile.From(Array.Empty<Holding>(), [sale]));

            var loaded = store.Load(PortfolioFile.FileName, () => new PortfolioFile());
            var snapshot = PnlCalculator.Calculate(
                loaded.ToHoldings(),
                new Dictionary<SecurityId, Quote> { [A] = QuoteFor(A, 110m, 100m) },
                MarketPhase.Trading,
                Today,
                Today,
                false,
                trades: loaded.ToTrades());

            Assert.Empty(loaded.ToHoldings());
            Assert.Equal(995m, snapshot.DayPnl);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Scheduler_fetches_a_cleared_position_and_does_not_repeat_it_next_day()
    {
        var time = FakeTime.AtBeijing(new DateTime(2026, 10, 28, 10, 0, 0));
        var provider = new FakeQuoteProvider();
        provider.Respond = ids => ids.Select(id =>
        {
            var now = BeijingTime.Now(time);
            return new Quote(id, id.Symbol, now.Day == 28 ? 110m : 125m,
                now.Day == 28 ? 100m : 120m, now);
        }).ToList();
        var scheduler = new QuoteScheduler(provider, new AlertEngine([5m], false), time);
        var sale = Record(TradeSide.Sell, 100, 110m, 5m);

        scheduler.SetHoldings([], [sale]);
        await scheduler.RunOnceAsync(default);

        Assert.Contains(A, provider.Requests.Single());
        Assert.Equal(995m, scheduler.Latest.DayPnl);

        time.SetBeijing(new DateTime(2026, 10, 29, 10, 0, 0));
        await scheduler.RunOnceAsync(default);

        Assert.Equal(0m, scheduler.Latest.DayPnl);
    }

    [Fact]
    public void Tray_tooltip_displays_realized_pnl_after_clearing()
    {
        var snapshot = Calc(
            [],
            [Record(TradeSide.Sell, 100, 110m, 5m)],
            QuoteFor(A, 110m, 100m));

        var tooltip = DisplayFormat.TrayTooltip(snapshot, TradeDate, alertsPaused: false);

        Assert.Contains("+995.00", tooltip);
        Assert.DoesNotContain("尚未导入持仓", tooltip);
    }
}

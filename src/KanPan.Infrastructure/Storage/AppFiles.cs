using KanPan.Core.Alerts;
using KanPan.Core.Models;
using KanPan.Core.Trades;

namespace KanPan.Infrastructure.Storage;

public enum ColorScheme { RedUp, GreenUp }

public sealed class AppSettings
{
    public const string FileName = "settings.json";

    public int Version { get; set; } = 1;
    public int RefreshSeconds { get; set; } = 5;
    public List<decimal> AlertLevels { get; set; } = [5m, 7m, 9.5m];
    public bool RealertOnRetreat { get; set; }
    public ColorScheme ColorScheme { get; set; } = ColorScheme.RedUp;
    public DateOnly? AlertsPausedOn { get; set; }
    public bool TrayHintShown { get; set; }

    /// <summary>No pop-ups and no P&L on the tray icon; quotes keep updating in the main window.</summary>
    public bool WorkMode { get; set; }

    public bool AutoExitEnabled { get; set; } = true;

    /// <summary>Beijing time.</summary>
    public TimeOnly AutoExitTime { get; set; } = new(15, 10);

    /// <summary>Last choice in the trade dialog for how sells affect cost.</summary>
    public CostMethod CostMethod { get; set; } = CostMethod.Diluted;

    /// <summary>Last HKD/CNY rate fetched, used until a fresh one arrives (or when offline).</summary>
    public decimal? HkdCnyRate { get; set; }
}

public sealed class PortfolioFile
{
    public const string FileName = "portfolio.json";

    public int Version { get; set; } = 1;
    public List<HoldingEntry> Holdings { get; set; } = [];
    public List<TradeEntry> Trades { get; set; } = [];

    public sealed class TradeEntry
    {
        public DateOnly Date { get; set; }
        public string Symbol { get; set; } = "";
        public TradeSide Side { get; set; }
        public long Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Fee { get; set; }
    }

    public sealed class HoldingEntry
    {
        public string Symbol { get; set; } = "";
        public long Shares { get; set; }
        public decimal? CostPrice { get; set; }
    }

    public IReadOnlyList<Holding> ToHoldings() =>
        Holdings
            .Where(e => e.Shares > 0 && SecurityId.TryParse(e.Symbol, out _, out _))
            .Select(e => new Holding(SecurityId.Parse(e.Symbol), e.Shares, e.CostPrice))
            .ToList();

    public IReadOnlyList<RecordedTrade> ToTrades() =>
        Trades.Where(e => e.Quantity > 0 && e.Price > 0 && e.Fee >= 0 && SecurityId.TryParse(e.Symbol, out _, out _))
            .Select(e => new RecordedTrade(e.Date, new Trade(SecurityId.Parse(e.Symbol), e.Side, e.Quantity, e.Price, e.Fee)))
            .ToList();

    public static PortfolioFile From(IEnumerable<Holding> holdings, IEnumerable<RecordedTrade>? trades = null) => new()
    {
        Holdings = holdings
            .Select(h => new HoldingEntry { Symbol = h.Id.Symbol, Shares = h.Shares, CostPrice = h.CostPrice })
            .ToList(),
        Trades = (trades ?? []).Select(t => new TradeEntry
        {
            Date = t.Date, Symbol = t.Trade.Id.Symbol, Side = t.Trade.Side,
            Quantity = t.Trade.Quantity, Price = t.Trade.Price, Fee = t.Trade.Fee,
        }).ToList(),
    };
}

public static class AlertStateFile
{
    public const string FileName = "alert-state.json";

    public static AlertState Load(JsonStore store) => store.Load(FileName, () => new AlertState());

    public static void Save(JsonStore store, AlertState state) => store.Save(FileName, state);
}

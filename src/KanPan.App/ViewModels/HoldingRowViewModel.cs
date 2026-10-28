using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KanPan.App.Services;
using KanPan.Core.Display;
using KanPan.Core.Models;

namespace KanPan.App.ViewModels;

/// <summary>One holding row. Numeric properties back column sorting; *Text properties are what is displayed.</summary>
public sealed partial class HoldingRowViewModel(SecurityId id) : ObservableObject
{
    public SecurityId Id { get; } = id;
    public string Code => Id.Code;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _priceText = "--";
    [ObservableProperty] private decimal _changePct;
    [ObservableProperty] private string _changePctText = "--";
    [ObservableProperty] private long _shares;
    [ObservableProperty] private decimal _marketValue;
    [ObservableProperty] private string _marketValueText = "--";
    [ObservableProperty] private decimal _dayPnl;
    [ObservableProperty] private string _dayPnlText = "--";
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private decimal _totalPnl;
    [ObservableProperty] private string _totalPnlText = "";
    [ObservableProperty] private Brush? _changeBrush;
    [ObservableProperty] private Brush? _totalBrush;
    [ObservableProperty] private Brush? _rowBackground;
    [ObservableProperty] private bool _isAlert;
    [ObservableProperty] private string _note = "";

    public void Update(HoldingView v, Palette palette, decimal alertThreshold)
    {
        var q = v.Quote;
        Shares = v.Holding.Shares;
        CostText = v.Holding.CostPrice is { } c ? DisplayFormat.Price(c) : "";

        if (q is null)
        {
            Name = "（无行情）";
            PriceText = ChangePctText = MarketValueText = DayPnlText = "--";
            TotalPnlText = "";
            ChangePct = MarketValue = DayPnl = TotalPnl = 0;
            ChangeBrush = TotalBrush = palette.NeutralBrush;
            RowBackground = null;
            IsAlert = false;
            Note = "未查到行情，请检查代码";
            return;
        }

        Name = q.Name;
        PriceText = DisplayFormat.Price(q.Price);
        ChangePct = v.DayPct;
        ChangePctText = v.IsCurrent ? DisplayFormat.SignedPct(v.DayPct) : "--";
        ChangeBrush = palette.ForValue(v.DayPct);
        IsAlert = v.IsCurrent && !q.IsSuspended && Math.Abs(v.DayPct) >= alertThreshold;
        RowBackground = IsAlert ? (v.DayPct > 0 ? palette.UpRowBrush : palette.DownRowBrush) : null;

        if (v.FxRate is null)
        {
            MarketValueText = DayPnlText = "--";
            TotalPnlText = "";
            MarketValue = DayPnl = TotalPnl = 0;
            TotalBrush = palette.NeutralBrush;
            Note = "暂无港币汇率，未计入合计";
            return;
        }

        MarketValue = v.MarketValue;
        MarketValueText = DisplayFormat.Amount(v.MarketValue);
        DayPnl = v.DayPnl;
        DayPnlText = DisplayFormat.SignedAmount(v.DayPnl);
        TotalPnl = v.TotalPnl ?? 0;
        TotalPnlText = v.TotalPnl is { } tp ? DisplayFormat.PnlWithPct(tp, v.TotalPct) : "";
        TotalBrush = palette.ForValue(v.TotalPnl ?? 0);
        Note = q.IsSuspended ? "停牌" : v.IsCurrent ? "" : $"行情停留在 {q.QuoteTime:MM-dd}";
    }
}

using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KanPan.App.Services;
using KanPan.Core.Display;
using KanPan.Core.Models;

namespace KanPan.App.ViewModels;

public sealed partial class MainViewModel(Palette palette, decimal alertThreshold) : ObservableObject
{
    public ObservableCollection<HoldingRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _dayLabel = "当日盈亏";
    [ObservableProperty] private string _dayPnlText = "--";
    [ObservableProperty] private string _dayPctText = "";
    [ObservableProperty] private Brush? _dayBrush;
    [ObservableProperty] private string _marketValueText = "--";
    [ObservableProperty] private bool _hasTotal;
    [ObservableProperty] private string _totalPnlText = "";
    [ObservableProperty] private string _totalNote = "";
    [ObservableProperty] private Brush? _totalBrush;
    [ObservableProperty] private string _statusText = "正在获取行情…";
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isWorkMode;

    public event Action<bool>? WorkModeChanged;

    partial void OnIsWorkModeChanged(bool value) => WorkModeChanged?.Invoke(value);

    public void Update(PortfolioSnapshot s, DateOnly today)
    {
        IsEmpty = s.Items.Count == 0;
        DayLabel = DisplayFormat.DayLabel(s.SessionDate, today);
        DayPnlText = DisplayFormat.SignedAmount(s.DayPnl);
        DayPctText = DisplayFormat.SignedPct(s.DayPct);
        DayBrush = palette.ForValue(s.DayPnl);
        MarketValueText = DisplayFormat.Amount(s.TotalMarketValue);

        HasTotal = s.TotalPnl is not null;
        TotalPnlText = s.TotalPnl is { } tp ? DisplayFormat.PnlWithPct(tp, s.TotalPct) : "";
        TotalBrush = palette.ForValue(s.TotalPnl ?? 0);
        var withQuote = s.Items.Count(i => i.Quote is not null);
        TotalNote = HasTotal && s.CostCoveredCount < withQuote ? $"（仅含 {s.CostCoveredCount} 只填了成本价的持仓）" : "";

        IsStale = s.IsStale;
        StatusText = s.IsStale
            ? s.LastSuccessAt is { } t ? $"网络异常，数据停留在 {t:HH:mm:ss}，正在重试…" : "网络异常，正在重试…"
            : $"{DisplayFormat.PhaseText(s.Phase)}  ·  更新于 {s.UpdatedAt:HH:mm:ss}";
        if (s.HasHongKong)
            StatusText += s.HkdCnyRate is { } r
                ? $"  ·  港股现价、成本价为港元，金额按 1 港元 = {r:0.####} 元折算"
                : "  ·  暂无港币汇率，港股未计入合计";

        SyncRows(s.Items);
    }

    private void SyncRows(IReadOnlyList<HoldingView> items)
    {
        var wanted = items.Select(i => i.Holding.Id).ToHashSet();
        for (var i = Rows.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Rows[i].Id)) Rows.RemoveAt(i);

        var byId = Rows.ToDictionary(r => r.Id);
        foreach (var item in items)
        {
            if (!byId.TryGetValue(item.Holding.Id, out var row))
            {
                row = new HoldingRowViewModel(item.Holding.Id);
                Rows.Add(row);
            }
            row.Update(item, palette, alertThreshold);
        }
    }
}

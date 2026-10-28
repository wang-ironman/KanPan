using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KanPan.Core.Display;
using KanPan.Core.Models;
using KanPan.Core.Quotes;
using KanPan.Core.Trades;

namespace KanPan.App.Views;

/// <summary>Records one buy or sell against a holding (or opens a new one) and previews the resulting shares and cost.</summary>
public partial class TradeWindow : Window
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
    private static readonly Brush HintBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

    private readonly IReadOnlyList<Holding> _holdings;
    private readonly IReadOnlyDictionary<SecurityId, Quote> _quotes;
    private readonly IQuoteProvider _provider;
    private readonly List<StockOption> _options;
    private readonly Dictionary<SecurityId, Lookup> _lookups = new();
    private SecurityId? _currentId;
    private bool _ready;

    public TradeWindow(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<SecurityId, Quote> quotes,
        IQuoteProvider provider,
        CostMethod method,
        SecurityId? preselect)
    {
        InitializeComponent();
        _holdings = holdings;
        _quotes = quotes;
        _provider = provider;
        _options = holdings.Select(h => new StockOption(h, quotes.GetValueOrDefault(h.Id))).ToList();

        StockBox.ItemsSource = _options;
        StockBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => OnStockChanged()));
        (method == CostMethod.WeightedAverage ? AverageOption : DilutedOption).IsChecked = true;
        if (preselect is { } id && _options.FirstOrDefault(o => o.Holding.Id == id) is { } option)
            StockBox.SelectedItem = option;

        _ready = true;
        OnStockChanged();
        Loaded += (_, _) => (preselect is null ? (Control)StockBox : PriceBox).Focus();
    }

    public Trade? Trade { get; private set; }

    /// <summary>The holding after the trade; null when the position was closed.</summary>
    public Holding? After { get; private set; }

    public CostMethod Method => AverageOption.IsChecked == true ? CostMethod.WeightedAverage : CostMethod.Diluted;

    private bool IsSell => SellOption.IsChecked == true;

    private void OnInputChanged(object sender, RoutedEventArgs e) => Recalculate();

    private void OnStockSelectionChanged(object sender, SelectionChangedEventArgs e) => OnStockChanged();

    private void OnStockChanged()
    {
        if (!_ready) return;
        var id = ResolveStock(out _);
        if (id != _currentId)
        {
            _currentId = id;
            if (id is { } newId)
            {
                if (PriceBox.Text.Length == 0 && QuoteFor(newId) is { } q)
                    PriceBox.Text = DisplayFormat.Price(q.Price).Replace(",", "");
                if (!_quotes.ContainsKey(newId) && !_lookups.ContainsKey(newId))
                    _ = LookupAsync(newId);
            }
        }
        Recalculate();
    }

    private SecurityId? ResolveStock(out string? error)
    {
        error = null;
        var text = StockBox.Text.Trim();
        if (StockBox.SelectedItem is StockOption selected && selected.ToString() == StockBox.Text)
            return selected.Holding.Id;
        if (text.Length == 0) return null;

        var byName = _options.Where(o => o.Name == text || o.ToString() == text).ToList();
        if (byName.Count == 1) return byName[0].Holding.Id;

        var token = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        return SecurityId.TryParse(token, out var id, out error) ? id : null;
    }

    private Quote? QuoteFor(SecurityId id) =>
        _quotes.GetValueOrDefault(id) ?? (_lookups.TryGetValue(id, out var l) ? l.Quote : null);

    private async Task LookupAsync(SecurityId id)
    {
        _lookups[id] = Lookup.Pending;
        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var quotes = await _provider.GetQuotesAsync([id], cts.Token);
            _lookups[id] = new Lookup(quotes.FirstOrDefault(q => q.Id == id), Done: true, Failed: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or FormatException)
        {
            _lookups[id] = new Lookup(null, Done: true, Failed: true);
        }

        if (_currentId == id)
        {
            if (PriceBox.Text.Length == 0 && _lookups[id].Quote is { } q)
                PriceBox.Text = DisplayFormat.Price(q.Price).Replace(",", "");
            Recalculate();
        }
    }

    private void Recalculate()
    {
        if (!_ready) return;

        var sell = IsSell;
        SellAllButton.Visibility = sell ? Visibility.Visible : Visibility.Collapsed;
        MethodLabel.Visibility = MethodPanel.Visibility = sell ? Visibility.Visible : Visibility.Collapsed;

        var id = ResolveStock(out var codeError);
        var unit = id?.CurrencyUnit ?? "元";
        PriceUnit.Text = unit;
        FeeUnit.Text = $"{unit}，含佣金和印花税，可不填";
        var before = id is { } i ? _holdings.FirstOrDefault(h => h.Id == i) : null;
        var quote = id is { } qi ? QuoteFor(qi) : null;
        UseQuoteButton.IsEnabled = quote is not null;
        SellAllButton.IsEnabled = before is not null;

        SaveButton.IsEnabled = false;
        WarningText.Visibility = Visibility.Collapsed;
        BeforeText.Text = before is null
            ? "交易前：未持有"
            : $"交易前：{before.Shares:N0} 股，成本 {FormatCost(before.CostPrice)}";

        if (!DescribeStock(id, codeError, before, quote))
        {
            ShowAfter("", NormalBrush);
            return;
        }

        if (!TryReadNumbers(out var price, out var quantity, out var fee, out var inputError))
        {
            ShowAfter(inputError, inputError.StartsWith("请") ? HintBrush : ErrorBrush);
            return;
        }

        var trade = new Trade(id!.Value, sell ? TradeSide.Sell : TradeSide.Buy, quantity, price, fee);
        var result = TradeCalculator.Apply(before, trade, Method);
        if (!result.IsValid)
        {
            ShowAfter(result.Error!, ErrorBrush);
            return;
        }

        var amount = $"成交金额 {DisplayFormat.Amount(quantity * price)} {unit}";
        ShowAfter(result.After is { } after
                ? $"交易后：{after.Shares:N0} 股，成本 {FormatCost(after.CostPrice)}（{amount}）"
                : $"交易后：清仓，将从持仓中移除（{amount}）",
            NormalBrush);
        if (result.Warning is { } w)
        {
            WarningText.Text = w;
            WarningText.Visibility = Visibility.Visible;
        }

        Trade = trade;
        After = result.After;
        SaveButton.IsEnabled = true;
    }

    /// <summary>Updates the line under the stock box; returns whether the stock can be traded.</summary>
    private bool DescribeStock(SecurityId? id, string? codeError, Holding? before, Quote? quote)
    {
        StockInfo.Foreground = HintBrush;
        if (id is null)
        {
            StockInfo.Text = codeError is null ? "从下拉列表选择已有持仓，或直接输入代码建仓" : codeError;
            if (codeError is not null) StockInfo.Foreground = ErrorBrush;
            return false;
        }

        var priceText = quote is null ? "" : $" · 现价 {DisplayFormat.Price(quote.Price)}{(id.Value.IsHongKong ? " 港元" : "")}";
        if (before is not null)
        {
            StockInfo.Text = $"{quote?.Name ?? id.Value.Symbol}{priceText} · 已有持仓";
            return true;
        }

        if (_quotes.ContainsKey(id.Value))
        {
            StockInfo.Text = $"{quote!.Name}{priceText} · 新建仓";
            return true;
        }

        var lookup = _lookups.GetValueOrDefault(id.Value);
        if (lookup is null || !lookup.Done)
        {
            StockInfo.Text = $"{id.Value.Symbol} · 正在查询行情…";
            return false;
        }
        if (lookup.Failed)
        {
            StockInfo.Text = $"{id.Value.Symbol} · 网络异常，未能校验代码，仍可保存";
            return true;
        }
        if (lookup.Quote is null)
        {
            StockInfo.Text = $"{id.Value.Symbol} · 未查到行情，代码可能不存在";
            StockInfo.Foreground = ErrorBrush;
            return false;
        }
        StockInfo.Text = $"{lookup.Quote.Name}{priceText} · 新建仓";
        return true;
    }

    private bool TryReadNumbers(out decimal price, out long quantity, out decimal fee, out string error)
    {
        price = 0;
        quantity = 0;
        fee = 0;
        error = "";
        var priceText = PriceBox.Text.Trim();
        var quantityText = QuantityBox.Text.Trim();
        if (priceText.Length == 0 || quantityText.Length == 0)
        {
            error = "请填写成交价和数量";
            return false;
        }
        if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out price))
        {
            error = $"成交价不是有效数字：「{priceText}」";
            return false;
        }
        if (!long.TryParse(quantityText, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out quantity))
        {
            error = $"数量应为整数：「{quantityText}」";
            return false;
        }
        var feeText = FeeBox.Text.Trim();
        if (feeText.Length > 0 && !decimal.TryParse(feeText, NumberStyles.Number, CultureInfo.InvariantCulture, out fee))
        {
            error = $"手续费不是有效数字：「{feeText}」";
            return false;
        }
        return true;
    }

    private void ShowAfter(string text, Brush brush)
    {
        AfterText.Text = text;
        AfterText.Foreground = brush;
        Trade = null;
        After = null;
    }

    private static string FormatCost(decimal? cost) => cost is { } c ? DisplayFormat.Price(c) : "未填";

    private void OnUseQuoteClick(object sender, RoutedEventArgs e)
    {
        if (_currentId is { } id && QuoteFor(id) is { } q)
            PriceBox.Text = DisplayFormat.Price(q.Price).Replace(",", "");
    }

    private void OnSellAllClick(object sender, RoutedEventArgs e)
    {
        if (_currentId is { } id && _holdings.FirstOrDefault(h => h.Id == id) is { } h)
            QuantityBox.Text = h.Shares.ToString(CultureInfo.InvariantCulture);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Trade is null) return;
        DialogResult = true;
    }

    private sealed record Lookup(Quote? Quote, bool Done, bool Failed)
    {
        public static readonly Lookup Pending = new(null, false, false);
    }

    private sealed class StockOption(Holding holding, Quote? quote)
    {
        public Holding Holding { get; } = holding;
        public string Name { get; } = quote?.Name ?? holding.Id.Symbol;

        public override string ToString() => $"{Name}  {Holding.Id.Code}（持有 {Holding.Shares:N0} 股）";
    }
}

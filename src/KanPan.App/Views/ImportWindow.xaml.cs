using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using KanPan.Core.Import;
using KanPan.Core.Models;
using KanPan.Core.Quotes;
using KanPan.Infrastructure.Storage;
using Microsoft.Win32;

namespace KanPan.App.Views;

public partial class ImportWindow : Window
{
    private const string SampleText =
        "# 每行一只：代码, 股数[, 成本价]。# 开头的行会被忽略，可删掉这几行后粘贴\n" +
        "# 600519,100,1550\n" +
        "# 510300 10000\n" +
        "# 00700,200,380\n";

    private static readonly TimeSpan ValidateTimeout = TimeSpan.FromSeconds(8);

    private readonly IReadOnlyList<Holding> _existing;
    private readonly IQuoteProvider _quotes;
    private ParseResult? _parsed;
    private HashSet<SecurityId> _unknown = [];
    private bool _previewCurrent;
    private int _previewVersion;

    public ImportWindow(IReadOnlyList<Holding> existing, IQuoteProvider quotes)
    {
        InitializeComponent();
        _existing = existing;
        _quotes = quotes;

        InputBox.Text = existing.Count > 0 ? HoldingParser.Format(existing) + Environment.NewLine : SampleText.Replace("\n", Environment.NewLine);
        ModePanel.Visibility = existing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) =>
        {
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
            if (existing.Count > 0) await PreviewAsync();
        };
    }

    /// <summary>The holdings to save; set when the dialog closes with OK.</summary>
    public IReadOnlyList<Holding>? Result { get; private set; }

    private void OnInputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _previewCurrent = false;

    private async void OnPreviewClick(object sender, RoutedEventArgs e) => await PreviewAsync();

    private async void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV 或文本文件|*.csv;*.txt|所有文件|*.*", Title = "选择持仓文件" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            InputBox.Text = TextFiles.ReadAutoDetect(dialog.FileName);
            await PreviewAsync();
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"读取文件失败：{ex.Message}", "KanPan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!_previewCurrent) await PreviewAsync();
        if (_parsed is null) return;

        var valid = _parsed.Holdings.Where(h => !_unknown.Contains(h.Id)).ToList();
        var rejected = _parsed.Lines.Count(l => l.Status == ParseStatus.Error) + _unknown.Count;
        if (valid.Count == 0 && !(ReplaceMode.IsChecked == true && _parsed.Lines.Count == 0 && ConfirmClearAll()))
        {
            if (_parsed.Lines.Count > 0)
                MessageBox.Show(this, "没有可以保存的持仓，请检查标红的行。", "KanPan", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (rejected > 0 && MessageBox.Show(this,
                $"有 {rejected} 行无法导入（已标红），是否只保存其余 {valid.Count} 只？",
                "KanPan", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        Result = MergeMode.IsChecked == true ? Merge(_existing, valid) : valid;
        DialogResult = true;
    }

    private bool ConfirmClearAll() =>
        _existing.Count > 0 && MessageBox.Show(this, "内容为空，确定要清空全部持仓吗？", "KanPan",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static List<Holding> Merge(IReadOnlyList<Holding> existing, IReadOnlyList<Holding> imported)
    {
        var importedById = imported.ToDictionary(h => h.Id);
        var result = existing.Select(h => importedById.GetValueOrDefault(h.Id, h)).ToList();
        result.AddRange(imported.Where(h => existing.All(x => x.Id != h.Id)));
        return result;
    }

    private async Task PreviewAsync()
    {
        var version = ++_previewVersion;
        _parsed = HoldingParser.Parse(InputBox.Text);
        _unknown = [];
        _previewCurrent = true;

        var rows = _parsed.Lines.Select(PreviewRow.From).ToList();
        PreviewGrid.ItemsSource = rows;
        var ids = _parsed.Holdings.Select(h => h.Id).ToList();
        SummaryText.Text = ids.Count > 0 ? "正在查询行情以校验代码…" : Summary(rows);
        if (ids.Count == 0) return;

        PreviewButton.IsEnabled = SaveButton.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(ValidateTimeout);
            var quotes = (await _quotes.GetQuotesAsync(ids, cts.Token)).ToDictionary(q => q.Id);
            if (version != _previewVersion) return;

            _unknown = ids.Where(id => !quotes.ContainsKey(id)).ToHashSet();
            foreach (var row in rows.Where(r => r.Id is not null))
            {
                if (quotes.TryGetValue(row.Id!.Value, out var q))
                    row.Name = q.Name;
                else
                    row.MarkUnknown();
            }
            PreviewGrid.Items.Refresh();
            SummaryText.Text = Summary(rows);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or FormatException)
        {
            if (version == _previewVersion)
                SummaryText.Text = Summary(rows) + "（网络异常，未能校验代码是否存在）";
        }
        finally
        {
            if (version == _previewVersion)
                PreviewButton.IsEnabled = SaveButton.IsEnabled = true;
        }
    }

    private static string Summary(IReadOnlyList<PreviewRow> rows)
    {
        if (rows.Count == 0) return "没有内容";
        var errors = rows.Count(r => r.IsError);
        var merged = rows.Count(r => r.IsWarning);
        var ok = rows.Count - errors - merged;
        var parts = new List<string> { $"{ok} 只可导入" };
        if (merged > 0) parts.Add($"{merged} 行重复已合并");
        if (errors > 0) parts.Add($"{errors} 行有错误");
        return string.Join("，", parts);
    }

    private sealed class PreviewRow
    {
        public int Line { get; init; }
        public SecurityId? Id { get; init; }
        public string Symbol { get; init; } = "";
        public string Name { get; set; } = "";
        public string Shares { get; init; } = "";
        public string Cost { get; init; } = "";
        public string Status { get; private set; } = "";
        public bool IsError { get; private set; }
        public bool IsWarning { get; init; }

        public void MarkUnknown()
        {
            IsError = true;
            Status = "未查到行情，代码可能不存在";
        }

        public static PreviewRow From(ParsedLine l) => new()
        {
            Line = l.LineNumber,
            Id = l.Holding?.Id,
            Symbol = l.Holding?.Id.Symbol ?? l.Raw,
            Shares = l.Holding?.Shares.ToString("N0", CultureInfo.InvariantCulture) ?? "",
            Cost = l.Holding?.CostPrice?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
            Status = l.Status switch
            {
                ParseStatus.Ok => "✓",
                _ => l.Message ?? "",
            },
            IsError = l.Status == ParseStatus.Error,
            IsWarning = l.Status == ParseStatus.Merged,
        };
    }
}

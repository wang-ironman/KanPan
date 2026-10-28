using System.Text;
using KanPan.Core.Alerts;
using KanPan.Core.Models;
using KanPan.Infrastructure.Quotes;
using KanPan.Infrastructure.Storage;

namespace KanPan.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kanpan-tests-" + Guid.NewGuid().ToString("N"));
    private readonly JsonStore _store;

    public StorageTests() => _store = new JsonStore(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_returns_fallback()
    {
        var settings = _store.Load(AppSettings.FileName, () => new AppSettings(), out var corrupt);
        Assert.False(corrupt);
        Assert.Equal(5, settings.RefreshSeconds);
    }

    [Fact]
    public void Settings_round_trip_with_enum_as_text()
    {
        _store.Save(AppSettings.FileName, new AppSettings { ColorScheme = ColorScheme.GreenUp, AlertsPausedOn = new DateOnly(2026, 10, 21) });

        Assert.Contains("\"GreenUp\"", File.ReadAllText(Path.Combine(_dir, AppSettings.FileName)));
        var loaded = _store.Load(AppSettings.FileName, () => new AppSettings());
        Assert.Equal(ColorScheme.GreenUp, loaded.ColorScheme);
        Assert.Equal(new DateOnly(2026, 10, 21), loaded.AlertsPausedOn);
        Assert.False(File.Exists(Path.Combine(_dir, AppSettings.FileName + ".tmp")));
    }

    [Fact]
    public void Corrupt_file_is_backed_up()
    {
        File.WriteAllText(Path.Combine(_dir, PortfolioFile.FileName), "{ not json");

        var loaded = _store.Load(PortfolioFile.FileName, () => new PortfolioFile(), out var corrupt);

        Assert.True(corrupt);
        Assert.Empty(loaded.Holdings);
        Assert.True(File.Exists(Path.Combine(_dir, PortfolioFile.FileName + ".bak")));
    }

    [Fact]
    public void Portfolio_round_trip_skips_invalid_entries()
    {
        var file = PortfolioFile.From([new Holding(SecurityId.Parse("600519"), 100, 1550m), new Holding(SecurityId.Parse("510300"), 1000, null)]);
        file.Holdings.Add(new PortfolioFile.HoldingEntry { Symbol = "garbage", Shares = 1 });
        _store.Save(PortfolioFile.FileName, file);

        var holdings = _store.Load(PortfolioFile.FileName, () => new PortfolioFile()).ToHoldings();
        Assert.Equal(2, holdings.Count);
        Assert.Equal(1550m, holdings[0].CostPrice);
        Assert.Null(holdings[1].CostPrice);
    }

    [Fact]
    public void Alert_state_round_trip()
    {
        var state = new AlertState { TradeDate = new DateOnly(2026, 10, 21) };
        state.Marks["sh600519"] = new AlertMark { Up = 2 };
        AlertStateFile.Save(_store, state);

        var loaded = AlertStateFile.Load(_store);
        Assert.Equal(state.TradeDate, loaded.TradeDate);
        Assert.Equal(2, loaded.Marks["sh600519"].Up);
    }

    [Fact]
    public void Text_decoding_handles_utf8_bom_and_gbk()
    {
        const string text = "代码,股数\n600519,100";
        Assert.Equal(text, TextFiles.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]));
        Assert.Equal(text, TextFiles.Decode(HttpQuoteProvider.Gbk.GetBytes(text)));
    }
}

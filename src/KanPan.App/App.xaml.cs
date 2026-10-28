using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using KanPan.App.Services;
using KanPan.App.ViewModels;
using KanPan.App.Views;
using KanPan.Core;
using KanPan.Core.Alerts;
using KanPan.Core.Models;
using KanPan.Core.Quotes;
using KanPan.Core.Schedule;
using KanPan.Core.Trades;
using KanPan.Infrastructure.Logging;
using KanPan.Infrastructure.Quotes;
using KanPan.Infrastructure.Storage;
using Microsoft.Win32;

namespace KanPan.App;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private IAppLog _log = NullLog.Instance;
    private JsonStore _store = null!;
    private AppSettings _settings = null!;
    private IReadOnlyList<Holding> _holdings = [];
    private IReadOnlyList<RecordedTrade> _trades = [];
    private HttpClient _http = null!;
    private FailoverQuoteProvider _provider = null!;
    private AlertEngine _alertEngine = null!;
    private QuoteScheduler _scheduler = null!;
    private Palette _palette = null!;
    private MainViewModel _mainVm = null!;
    private MainWindow? _mainWindow;
    private TrayIconService _tray = null!;
    private Notifier _notifier = null!;
    private Window? _activeDialog;
    private AutoExitPolicy _autoExit = null!;
    private DispatcherTimer? _autoExitTimer;
    private bool _exiting;

    private static DateOnly Today => DateOnly.FromDateTime(BeijingTime.Now(TimeProvider.System));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDir = JsonStore.DefaultDirectory;
        var instanceScope = "KanPan";
#if DEBUG
        // Lets a debug build run beside the user's installed copy without touching its data.
        if (Environment.GetEnvironmentVariable("KANPAN_DATA_DIR") is { Length: > 0 } devDir)
        {
            dataDir = devDir;
            instanceScope = "KanPan.Dev";
        }
#endif

        _singleInstance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(ShowMainWindow), instanceScope);
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        _log = new FileLog(System.IO.Path.Combine(dataDir, "logs"));
        _log.Info($"KanPan 启动，数据目录 {dataDir}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("未观察到的任务异常", args.Exception);
            args.SetObserved();
        };

        _store = new JsonStore(dataDir, _log);
        _settings = _store.Load(AppSettings.FileName, () => new AppSettings(), out var settingsCorrupt);
        var portfolio = _store.Load(PortfolioFile.FileName, () => new PortfolioFile(), out var portfolioCorrupt);
        _holdings = portfolio.ToHoldings();
        _trades = portfolio.ToTrades();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) KanPan/0.1");
        var tencent = new TencentQuoteProvider(_http);
        var sina = new SinaQuoteProvider(_http);
        _provider = new FailoverQuoteProvider(tencent, sina, TimeProvider.System, _log);

        _alertEngine = CreateAlertEngine();
        _scheduler = new QuoteScheduler(_provider, _alertEngine, TimeProvider.System, _log, new FallbackFxRateProvider([tencent, sina]))
        {
            RefreshInterval = TimeSpan.FromSeconds(Math.Clamp(_settings.RefreshSeconds, 3, 60)),
            AlertsPausedOn = _settings.AlertsPausedOn,
            HkdCnyRate = _settings.HkdCnyRate,
        };

        _palette = new Palette(_settings.ColorScheme);
        _mainVm = new MainViewModel(_palette, _alertEngine.FirstThreshold);
        _tray = new TrayIconService(_palette, new TrayMenuActions
        {
            ToggleMainWindow = ToggleMainWindow,
            ShowMainWindow = ShowMainWindow,
            EditHoldings = EditHoldings,
            RecordTrade = () => RecordTrade(null),
            RefreshNow = _scheduler.RefreshNow,
            IsAlertsPaused = () => _settings.AlertsPausedOn == Today,
            SetAlertsPaused = SetAlertsPaused,
            IsWorkMode = () => _settings.WorkMode,
            SetWorkMode = on => _mainVm.IsWorkMode = on,
            IsAutoExitEnabled = () => _settings.AutoExitEnabled,
            SetAutoExit = SetAutoExit,
            AutoExitTime = () => _autoExit.ExitTime,
            Exit = ExitApp,
        });
        _mainVm.IsWorkMode = _settings.WorkMode;
        _mainVm.WorkModeChanged += OnWorkModeChanged;
        _notifier = new Notifier(_log, _tray.ShowBalloon, symbol => Dispatcher.BeginInvoke(() => ShowMainWindow(symbol)));

        _scheduler.SnapshotUpdated += s => Dispatcher.BeginInvoke(() =>
        {
            _mainVm.Update(s, Today);
            _tray.Update(s, Today);
        });
        _scheduler.AlertsRaised += (events, first) => Dispatcher.BeginInvoke(() => OnAlerts(events, first));
        // Raised on the scheduler thread, which is the only writer of the state, so saving here is race-free.
        _scheduler.AlertStateChanged += state => TrySave(() => AlertStateFile.Save(_store, state));
        _scheduler.HkdCnyRateUpdated += rate => Dispatcher.BeginInvoke(() =>
        {
            _settings.HkdCnyRate = rate;
            SaveSettings();
        });
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _scheduler.SetHoldings(_holdings, _trades);
        _scheduler.Start();
        StartAutoExitTimer();

        if (settingsCorrupt || portfolioCorrupt)
            MessageBox.Show("配置文件已损坏，已备份为 .bak 文件并恢复默认设置。\n" + dataDir, "KanPan", MessageBoxButton.OK, MessageBoxImage.Warning);

        if (_holdings.Count == 0)
        {
            EditHoldings();
        }
        else if (!_settings.TrayHintShown && !_settings.WorkMode)
        {
            _tray.ShowBalloon("KanPan 已在托盘运行", "单击托盘图标查看持仓，右键可编辑持仓或退出。");
            _settings.TrayHintShown = true;
            SaveSettings();
        }
    }

    private AlertEngine CreateAlertEngine()
    {
        var levels = _settings.AlertLevels.Where(l => l > 0).ToList();
        if (levels.Count == 0) levels = new AppSettings().AlertLevels;
        return new AlertEngine(levels, _settings.RealertOnRetreat, AlertStateFile.Load(_store));
    }

    /// <summary>
    /// In work mode alerts are only logged and highlighted in the main window. They still count as
    /// delivered, so leaving work mode does not replay them.
    /// </summary>
    private void OnAlerts(IReadOnlyList<AlertEvent> events, bool isFirstRound)
    {
        foreach (var message in AlertMessages.Compose(events, isFirstRound, _alertEngine.FirstThreshold))
        {
            _log.Info($"提醒{(_settings.WorkMode ? "（工作模式，未弹出）" : "")}：{message.Title} {message.Body.Replace('\n', ' ')}");
            if (!_settings.WorkMode) _notifier.Show(message);
        }
        if (!_settings.WorkMode && _mainWindow is not { IsVisible: true })
            _tray.StartBlink();
    }

    private void OnWorkModeChanged(bool on)
    {
        if (_settings.WorkMode == on) return;
        _settings.WorkMode = on;
        SaveSettings();
        _log.Info(on ? "开启工作模式" : "关闭工作模式");
        if (on) _tray.StopBlink();
        _tray.Refresh();
    }

#if DEBUG
    /// <summary>Fires a fake alert so notifications can be checked outside trading hours.</summary>
    private void FireTestAlert()
    {
        var first = _scheduler.Latest.Items.FirstOrDefault(i => i.Quote is not null)?.Quote;
        var id = first?.Id ?? SecurityId.Parse("600519");
        OnAlerts([new AlertEvent(id, first?.Name ?? "测试", 5.32m, first?.Price ?? 10m, 1, _alertEngine.FirstThreshold)], isFirstRound: false);
    }
#endif

    private MainWindow EnsureMainWindow()
    {
        if (_mainWindow is not null) return _mainWindow;
        _mainWindow = new MainWindow(_mainVm, EditHoldings, RecordTrade, _scheduler.RefreshNow);
#if DEBUG
        _mainWindow.EnableTestAlert(FireTestAlert);
#endif
        _mainWindow.HiddenToTray += () =>
        {
            if (_settings.TrayHintShown || _settings.WorkMode) return;
            _tray.ShowBalloon("KanPan 仍在运行", "窗口已收到托盘。右键托盘图标可以退出程序。");
            _settings.TrayHintShown = true;
            SaveSettings();
        };
        return _mainWindow;
    }

    private void ShowMainWindow() => ShowMainWindow(null);

    private void ShowMainWindow(string? focusSymbol)
    {
        if (_activeDialog is not null)
        {
            _activeDialog.Activate();
            return;
        }
        var window = EnsureMainWindow();
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        _tray.StopBlink();
        if (focusSymbol is not null) window.FocusSymbol(focusSymbol);
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow is { IsVisible: true, WindowState: not WindowState.Minimized, IsActive: true })
            _mainWindow.Hide();
        else
            ShowMainWindow();
    }

    /// <summary>Shows a modal dialog unless one is already open (then that one is brought forward).</summary>
    private bool? ShowDialogOnce(Window dialog)
    {
        if (_activeDialog is not null)
        {
            _activeDialog.Activate();
            return null;
        }
        _activeDialog = dialog;
        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            _activeDialog = null;
        }
    }

    private void EditHoldings()
    {
        var dialog = new ImportWindow(_holdings, _provider);
        if (ShowDialogOnce(dialog) == true && dialog.Result is { } holdings)
        {
            UpdateHoldings(holdings);
            _log.Info($"持仓已更新，共 {holdings.Count} 只");
            ShowMainWindow();
        }
    }

    private void RecordTrade(SecurityId? preselect)
    {
        var quotes = _scheduler.Latest.Items
            .Where(i => i.Quote is not null)
            .ToDictionary(i => i.Holding.Id, i => i.Quote!);
        var dialog = new TradeWindow(_holdings, quotes, _provider, _settings.CostMethod, preselect);
        if (ShowDialogOnce(dialog) != true || dialog.Trade is not { } trade) return;

        if (_settings.CostMethod != dialog.Method)
        {
            _settings.CostMethod = dialog.Method;
            SaveSettings();
        }

        var holdings = _holdings.ToList();
        var index = holdings.FindIndex(h => h.Id == trade.Id);
        if (dialog.After is not { } after)
            holdings.RemoveAt(index);
        else if (index >= 0)
            holdings[index] = after;
        else
            holdings.Add(after);

        var tradeDate = Today;
        _trades = [.. _trades.Where(t => t.Date == tradeDate), new RecordedTrade(tradeDate, trade)];
        UpdateHoldings(holdings);
        _log.Info($"记录交易：{(trade.Side == TradeSide.Buy ? "买入" : "卖出")} {trade.Id} {trade.Quantity} 股 @ {trade.Price}，手续费 {trade.Fee}，" +
                  (dialog.After is { } a ? $"交易后 {a.Shares} 股，成本 {a.CostPrice?.ToString() ?? "未填"}（{dialog.Method}）" : "已清仓"));
        ShowMainWindow(trade.Id.Symbol);
    }

    private void UpdateHoldings(IReadOnlyList<Holding> holdings)
    {
        var holdsHongKongBefore = _holdings.Any(h => h.Id.IsHongKong);
        _holdings = holdings;
        TrySave(() => _store.Save(PortfolioFile.FileName, PortfolioFile.From(holdings, _trades)));
        _scheduler.SetHoldings(holdings, _trades);
        if (holdings.Any(h => h.Id.IsHongKong) != holdsHongKongBefore) ArmAutoExit();
    }

    /// <summary>Re-arms from now, so a change made after the exit time does not quit straight away.</summary>
    private void ArmAutoExit()
    {
        var holdsHongKong = _holdings.Any(h => h.Id.IsHongKong);
        _autoExit = new AutoExitPolicy(AutoExitPolicy.Effective(_settings.AutoExitTime, holdsHongKong));
        _autoExit.ShouldExit(BeijingTime.Now(TimeProvider.System));
        _log.Info($"收盘后自动退出时间：工作日 {_autoExit.ExitTime:HH:mm}{(holdsHongKong ? "（持有港股）" : "")}");
    }

    private void StartAutoExitTimer()
    {
        ArmAutoExit();
        _autoExitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autoExitTimer.Tick += (_, _) =>
        {
            // Always fed so the policy knows whether the app was running before the exit time.
            if (!_autoExit.ShouldExit(BeijingTime.Now(TimeProvider.System)) || !_settings.AutoExitEnabled) return;
            if (_activeDialog is not null) return;
            _log.Info($"已过 {_autoExit.ExitTime:HH:mm}，收盘后自动退出");
            ExitApp();
        };
        _autoExitTimer.Start();
    }

    private void SetAutoExit(bool enabled)
    {
        _settings.AutoExitEnabled = enabled;
        ArmAutoExit();
        SaveSettings();
    }

    private void SetAlertsPaused(bool paused)
    {
        _settings.AlertsPausedOn = paused ? Today : null;
        _scheduler.AlertsPausedOn = _settings.AlertsPausedOn;
        SaveSettings();
        _tray.Refresh();
    }

    private void SaveSettings() => TrySave(() => _store.Save(AppSettings.FileName, _settings));

    private void TrySave(Action save)
    {
        try
        {
            save();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _log.Error("保存文件失败", ex);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _log.Info("系统唤醒，立即刷新");
            _scheduler.RefreshNow();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("界面线程未处理异常", e.Exception);
        e.Handled = true;
    }

    private async void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _log.Info("KanPan 退出");
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _autoExitTimer?.Stop();
        await _scheduler.DisposeAsync();
        _tray.Dispose();
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }
        _http.Dispose();
        _singleInstance?.Dispose();
        Shutdown();
    }
}

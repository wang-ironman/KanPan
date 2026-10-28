using System.Windows.Forms;
using System.Windows.Threading;
using KanPan.Core.Display;
using KanPan.Core.Models;
using Color = System.Drawing.Color;

namespace KanPan.App.Services;

public sealed class TrayMenuActions
{
    public required Action ToggleMainWindow { get; init; }
    public required Action ShowMainWindow { get; init; }
    public required Action EditHoldings { get; init; }
    public required Action RecordTrade { get; init; }
    public required Action RefreshNow { get; init; }
    public required Func<bool> IsAlertsPaused { get; init; }
    public required Action<bool> SetAlertsPaused { get; init; }
    public required Func<bool> IsWorkMode { get; init; }
    public required Action<bool> SetWorkMode { get; init; }
    public required Func<bool> IsAutoExitEnabled { get; init; }
    public required Action<bool> SetAutoExit { get; init; }
    public required Func<TimeOnly> AutoExitTime { get; init; }
    public required Action Exit { get; init; }
}

/// <summary>Tray icon showing the day change as a colored badge, plus the right-click menu. UI thread only.</summary>
public sealed class TrayIconService : IDisposable
{
    private static readonly Color AlertColor = Color.FromArgb(0xFF, 0xB3, 0x00);

    private readonly NotifyIcon _notifyIcon;
    private readonly Palette _palette;
    private readonly TrayMenuActions _actions;
    private readonly DispatcherTimer _blinkTimer;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _workModeItem;
    private readonly ToolStripMenuItem _autoExitItem;

    private RenderedIcon? _normalIcon;
    private RenderedIcon? _alertIcon;
    private (string Text, Color Color)? _normalKey;
    private bool _blinkOn;
    private PortfolioSnapshot? _lastSnapshot;
    private DateOnly _lastToday;

    public TrayIconService(Palette palette, TrayMenuActions actions)
    {
        _palette = palette;
        _actions = actions;

        _pauseItem = new ToolStripMenuItem("今日暂停提醒", null, (_, _) => _actions.SetAlertsPaused(!_actions.IsAlertsPaused()));
        _autoStartItem = new ToolStripMenuItem("开机自动启动", null, (_, _) => ToggleAutoStart());
        _workModeItem = new ToolStripMenuItem("工作模式（不弹提醒、不显示盈亏）", null, (_, _) => _actions.SetWorkMode(!_actions.IsWorkMode()));
        _autoExitItem = new ToolStripMenuItem("", null, (_, _) => _actions.SetAutoExit(!_actions.IsAutoExitEnabled()));

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("显示主窗口", null, (_, _) => _actions.ShowMainWindow()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add("记一笔交易…", null, (_, _) => _actions.RecordTrade());
        menu.Items.Add("编辑持仓…", null, (_, _) => _actions.EditHoldings());
        menu.Items.Add("立即刷新", null, (_, _) => _actions.RefreshNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_workModeItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_autoExitItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _actions.Exit());
        menu.Opening += (_, _) =>
        {
            _workModeItem.Checked = _actions.IsWorkMode();
            _pauseItem.Checked = _actions.IsAlertsPaused();
            _autoExitItem.Checked = _actions.IsAutoExitEnabled();
            _autoExitItem.Text = $"收盘后自动退出（工作日 {_actions.AutoExitTime():HH:mm}）";
            _autoStartItem.Checked = AutoStart.IsEnabled;
        };

        _normalIcon = TrayIconRenderer.Render("K", _palette.TrayColor(0, inactive: true));
        _notifyIcon = new NotifyIcon
        {
            Icon = _normalIcon.Icon,
            Text = "KanPan 正在启动…",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _actions.ToggleMainWindow();
        };
        _notifyIcon.BalloonTipClicked += (_, _) => _actions.ShowMainWindow();

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _blinkTimer.Tick += (_, _) => Blink();
    }

    public void Update(PortfolioSnapshot snapshot, DateOnly today)
    {
        _lastSnapshot = snapshot;
        _lastToday = today;

        if (_actions.IsWorkMode())
        {
            SetNormalIcon("K", _palette.TrayColor(0, inactive: true));
            _notifyIcon.Text = "KanPan（工作模式）";
            return;
        }

        var hasData = snapshot.SessionDate is not null;
        SetNormalIcon(
            hasData ? DisplayFormat.TrayBadge(snapshot.DayPct) : "K",
            _palette.TrayColor(snapshot.DayPct, inactive: !hasData || snapshot.IsStale));
        _notifyIcon.Text = DisplayFormat.TrayTooltip(snapshot, today, _actions.IsAlertsPaused());
    }

    /// <summary>Re-renders from the last snapshot, e.g. after work mode or the pause switch changes.</summary>
    public void Refresh()
    {
        if (_lastSnapshot is not null) Update(_lastSnapshot, _lastToday);
    }

    private void SetNormalIcon(string text, Color color)
    {
        if (_normalKey != (text, color))
        {
            var old = _normalIcon;
            _normalIcon = TrayIconRenderer.Render(text, color);
            _normalKey = (text, color);
            if (!_blinkTimer.IsEnabled || !_blinkOn) _notifyIcon.Icon = _normalIcon.Icon;
            old?.Dispose();
        }
    }

    public void StartBlink()
    {
        _alertIcon ??= TrayIconRenderer.Render("!", AlertColor, Color.Black);
        _blinkTimer.Start();
    }

    public void StopBlink()
    {
        _blinkTimer.Stop();
        _blinkOn = false;
        if (_normalIcon is not null) _notifyIcon.Icon = _normalIcon.Icon;
    }

    public void ShowBalloon(string title, string text) =>
        _notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    private void Blink()
    {
        _blinkOn = !_blinkOn;
        var icon = _blinkOn ? _alertIcon : _normalIcon;
        if (icon is not null) _notifyIcon.Icon = icon.Icon;
    }

    private void ToggleAutoStart()
    {
        try
        {
            AutoStart.Set(!AutoStart.IsEnabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowBalloon("无法设置开机启动", ex.Message);
        }
    }

    public void Dispose()
    {
        _blinkTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _normalIcon?.Dispose();
        _alertIcon?.Dispose();
    }
}

using KanPan.Core;
using KanPan.Core.Alerts;
using Microsoft.Toolkit.Uwp.Notifications;

namespace KanPan.App.Services;

/// <summary>Windows toast notifications, falling back to a tray balloon if toasts are unavailable.</summary>
public sealed class Notifier
{
    private const string SymbolArg = "symbol";

    private readonly IAppLog _log;
    private readonly Action<string, string> _balloonFallback;
    private bool _toastBroken;

    /// <param name="onActivated">Called on a thread-pool thread with the clicked alert's symbol (null for summaries).</param>
    public Notifier(IAppLog log, Action<string, string> balloonFallback, Action<string?> onActivated)
    {
        _log = log;
        _balloonFallback = balloonFallback;
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            onActivated(args.TryGetValue(SymbolArg, out var s) && s.Length > 0 ? s : null);
        };
    }

    public void Show(AlertMessage message)
    {
        if (!_toastBroken)
        {
            try
            {
                new ToastContentBuilder()
                    .AddArgument(SymbolArg, message.Symbol ?? "")
                    .AddText(message.Title)
                    .AddText(message.Body)
                    .Show();
                return;
            }
            catch (Exception ex)
            {
                _toastBroken = true;
                _log.Error("系统通知不可用，改用托盘气泡提示", ex);
            }
        }
        _balloonFallback(message.Title, message.Body);
    }
}

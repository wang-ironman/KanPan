using KanPan.Core;
using KanPan.Core.Models;
using KanPan.Core.Quotes;

namespace KanPan.Infrastructure.Quotes;

/// <summary>
/// Uses the primary source until it fails <see cref="FailuresBeforeSwitch"/> times in a row, then the backup.
/// While on the backup, the primary is retried every <see cref="ProbeInterval"/>.
/// </summary>
public sealed class FailoverQuoteProvider : IQuoteProvider
{
    public const int FailuresBeforeSwitch = 3;
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromMinutes(5);

    private readonly IQuoteProvider _primary;
    private readonly IQuoteProvider _backup;
    private readonly TimeProvider _time;
    private readonly IAppLog _log;
    private int _primaryFailures;
    private DateTimeOffset _lastProbe;

    public FailoverQuoteProvider(IQuoteProvider primary, IQuoteProvider backup, TimeProvider time, IAppLog? log = null)
    {
        _primary = primary;
        _backup = backup;
        _time = time;
        _log = log ?? NullLog.Instance;
    }

    public bool UsingBackup => _primaryFailures >= FailuresBeforeSwitch;

    public string Name => UsingBackup ? _backup.Name : _primary.Name;

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<SecurityId> ids, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (!UsingBackup || now - _lastProbe >= ProbeInterval)
        {
            if (UsingBackup) _lastProbe = now;
            try
            {
                var quotes = await _primary.GetQuotesAsync(ids, ct);
                if (UsingBackup) _log.Info($"主数据源 {_primary.Name} 已恢复");
                _primaryFailures = 0;
                return quotes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _primaryFailures++;
                if (!UsingBackup) throw;
                if (_primaryFailures == FailuresBeforeSwitch)
                {
                    _lastProbe = now;
                    _log.Error($"主数据源 {_primary.Name} 连续失败 {FailuresBeforeSwitch} 次，切换到 {_backup.Name}", ex);
                }
            }
        }
        return await _backup.GetQuotesAsync(ids, ct);
    }
}

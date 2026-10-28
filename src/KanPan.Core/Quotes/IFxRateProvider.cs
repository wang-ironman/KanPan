namespace KanPan.Core.Quotes;

public interface IFxRateProvider
{
    string Name { get; }

    /// <summary>CNY per 1 HKD, e.g. 0.8506. Throws on network or format failures.</summary>
    Task<decimal> GetHkdCnyAsync(CancellationToken ct);
}

/// <summary>Tries each source in order and returns the first rate obtained.</summary>
public sealed class FallbackFxRateProvider(IReadOnlyList<IFxRateProvider> sources) : IFxRateProvider
{
    public string Name => string.Join("/", sources.Select(s => s.Name));

    public async Task<decimal> GetHkdCnyAsync(CancellationToken ct)
    {
        Exception? last = null;
        foreach (var source in sources)
        {
            try
            {
                return await source.GetHkdCnyAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                last = ex;
            }
        }
        throw last ?? new InvalidOperationException("没有可用的汇率数据源");
    }
}

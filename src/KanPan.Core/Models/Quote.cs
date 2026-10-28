namespace KanPan.Core.Models;

/// <param name="QuoteTime">Beijing time reported by the data source.</param>
public sealed record Quote(
    SecurityId Id,
    string Name,
    decimal Price,
    decimal PrevClose,
    DateTime QuoteTime,
    bool IsSuspended = false)
{
    /// <summary>Percent change against previous close, e.g. 1.28 means +1.28%.</summary>
    public decimal ChangePct => PrevClose == 0 ? 0 : (Price - PrevClose) / PrevClose * 100m;

    /// <summary>
    /// Normalizes a raw quote: a zero price (suspended or not yet traded) falls back to the previous close.
    /// Returns null when the previous close is missing, because no change can be computed from it.
    /// </summary>
    public static Quote? Create(SecurityId id, string name, decimal price, decimal prevClose, DateTime quoteTime)
    {
        if (prevClose <= 0)
            return null;
        return price > 0
            ? new Quote(id, name, price, prevClose, quoteTime)
            : new Quote(id, name, prevClose, prevClose, quoteTime, IsSuspended: true);
    }
}

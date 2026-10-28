using KanPan.Core.Models;

namespace KanPan.Core.Quotes;

public interface IQuoteProvider
{
    string Name { get; }

    /// <summary>
    /// Returns quotes for the ids the source recognizes; unknown codes are simply absent from the result.
    /// Throws on network or format failures.
    /// </summary>
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<SecurityId> ids, CancellationToken ct);
}

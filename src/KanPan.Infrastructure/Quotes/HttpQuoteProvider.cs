using System.Text;
using KanPan.Core.Models;
using KanPan.Core.Quotes;

namespace KanPan.Infrastructure.Quotes;

/// <summary>Shared batching and GBK decoding for the plain-text quote endpoints.</summary>
public abstract class HttpQuoteProvider : IQuoteProvider, IFxRateProvider
{
    public const int BatchSize = 60;

    static HttpQuoteProvider() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Gbk => Encoding.GetEncoding(936);

    private readonly HttpClient _http;

    protected HttpQuoteProvider(HttpClient http) => _http = http;

    public abstract string Name { get; }

    protected abstract HttpRequestMessage BuildRequest(IEnumerable<SecurityId> ids);

    protected abstract IReadOnlyList<Quote> Parse(string body);

    protected abstract HttpRequestMessage BuildFxRequest();

    protected abstract decimal ParseFx(string body);

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<SecurityId> ids, CancellationToken ct)
    {
        var result = new List<Quote>(ids.Count);
        foreach (var batch in ids.Distinct().Chunk(BatchSize))
        {
            using var request = BuildRequest(batch);
            result.AddRange(Parse(await GetTextAsync(request, ct)));
        }
        return result;
    }

    public async Task<decimal> GetHkdCnyAsync(CancellationToken ct)
    {
        using var request = BuildFxRequest();
        return ParseFx(await GetTextAsync(request, ct));
    }

    private async Task<string> GetTextAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return Gbk.GetString(await response.Content.ReadAsByteArrayAsync(ct));
    }

    /// <summary>Loose sanity bounds for CNY per HKD; a value outside them means the response was misread.</summary>
    protected static decimal CheckRate(decimal rate, string raw) =>
        rate is > 0.5m and < 1.5m ? rate : throw new FormatException($"港币汇率数值异常：{raw}");

    /// <summary>Hong Kong prices come padded to 3 decimals ("382.400"); drop the padding so they display like the market quotes them.</summary>
    protected static decimal TrimPrice(SecurityId id, decimal price) =>
        id.IsHongKong ? price / 1.000000000000000000000000000000000m : price;
}

using System.Globalization;
using System.Text.RegularExpressions;
using KanPan.Core.Models;

namespace KanPan.Infrastructure.Quotes;

/// <summary>
/// qt.gtimg.cn. Each line looks like <c>v_sh600519="1~贵州茅台~600519~1309.69~1331.88~...~20261021152623~...";</c>
/// Hong Kong uses the real-time <c>r_hk00700</c> symbol (plain <c>hk00700</c> is 15 minutes delayed); the fields
/// are the same except the time, which reads <c>2026/10/22 16:08:40</c>. Unknown codes are omitted from the response.
/// </summary>
public sealed partial class TencentQuoteProvider(HttpClient http) : HttpQuoteProvider(http)
{
    private const int NameField = 1, PriceField = 3, PrevCloseField = 4, TimeField = 30;
    private const int FxRateField = 3;
    private static readonly string[] TimeFormats = ["yyyyMMddHHmmss", "yyyy/MM/dd HH:mm:ss"];

    public override string Name => "腾讯";

    protected override HttpRequestMessage BuildRequest(IEnumerable<SecurityId> ids) =>
        new(HttpMethod.Get, "https://qt.gtimg.cn/q=" + string.Join(',', ids.Select(i => (i.IsHongKong ? "r_" : "") + i.Symbol)));

    protected override IReadOnlyList<Quote> Parse(string body) => ParseBody(body);

    protected override HttpRequestMessage BuildFxRequest() => new(HttpMethod.Get, "https://qt.gtimg.cn/q=whHKDCNY");

    protected override decimal ParseFx(string body) => ParseFxBody(body);

    [GeneratedRegex(@"v_(?:r_)?(?<sym>[a-z]{2}\d+)=""(?<data>[^""]*)""")]
    private static partial Regex Line();

    [GeneratedRegex(@"v_whHKDCNY=""(?<data>[^""]*)""")]
    private static partial Regex FxLine();

    public static IReadOnlyList<Quote> ParseBody(string body)
    {
        var quotes = new List<Quote>();
        foreach (Match m in Line().Matches(body))
        {
            var f = m.Groups["data"].Value.Split('~');
            if (f.Length <= TimeField) continue;
            if (!SecurityId.TryParse(m.Groups["sym"].Value, out var id, out _)) continue;

            if (!decimal.TryParse(f[PriceField], NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                || !decimal.TryParse(f[PrevCloseField], NumberStyles.Number, CultureInfo.InvariantCulture, out var prev)
                || !DateTime.TryParseExact(f[TimeField], TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new FormatException($"腾讯行情格式无法解析：{m.Value}");

            if (Quote.Create(id, f[NameField], TrimPrice(id, price), TrimPrice(id, prev), time) is { } q)
                quotes.Add(q);
        }
        return quotes;
    }

    /// <summary><c>v_whHKDCNY="310~港元人民币~HKDCNY~0.8506~...";</c></summary>
    public static decimal ParseFxBody(string body)
    {
        var m = FxLine().Match(body);
        var f = m.Success ? m.Groups["data"].Value.Split('~') : [];
        if (f.Length <= FxRateField
            || !decimal.TryParse(f[FxRateField], NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
            throw new FormatException($"腾讯汇率格式无法解析：{body}");
        return CheckRate(rate, m.Value);
    }
}

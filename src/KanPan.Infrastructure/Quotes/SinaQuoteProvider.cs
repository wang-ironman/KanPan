using System.Globalization;
using System.Text.RegularExpressions;
using KanPan.Core.Models;

namespace KanPan.Infrastructure.Quotes;

/// <summary>
/// hq.sinajs.cn. Each line looks like <c>var hq_str_sh600519="贵州茅台,open,prevClose,price,...,2026-10-21,15:26:28,00,...";</c>
/// Hong Kong uses the real-time <c>rt_hk00700</c> symbol with its own layout:
/// <c>"TENCENT,腾讯控股,open,prevClose,high,low,price,...,2026/10/22,16:08:40,..."</c>.
/// Requires a Referer header. Unknown codes come back as an empty string; long-suspended codes come back all zeros.
/// </summary>
public sealed partial class SinaQuoteProvider(HttpClient http) : HttpQuoteProvider(http)
{
    private static readonly Layout AShare = new(Name: 0, PrevClose: 2, Price: 3, Date: 30, Time: 31, "yyyy-MM-dd HH:mm:ss");
    private static readonly Layout HongKong = new(Name: 1, PrevClose: 3, Price: 6, Date: 17, Time: 18, "yyyy/MM/dd HH:mm:ss");
    private const int FxRateField = 8;

    public override string Name => "新浪";

    protected override HttpRequestMessage BuildRequest(IEnumerable<SecurityId> ids) =>
        WithReferer("https://hq.sinajs.cn/list=" + string.Join(',', ids.Select(i => (i.IsHongKong ? "rt_" : "") + i.Symbol)));

    protected override IReadOnlyList<Quote> Parse(string body) => ParseBody(body);

    protected override HttpRequestMessage BuildFxRequest() => WithReferer("https://hq.sinajs.cn/list=fx_shkdcny");

    protected override decimal ParseFx(string body) => ParseFxBody(body);

    private static HttpRequestMessage WithReferer(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://finance.sina.com.cn");
        return request;
    }

    [GeneratedRegex(@"hq_str_(?:rt_)?(?<sym>[a-z]{2}\d+)=""(?<data>[^""]*)""")]
    private static partial Regex Line();

    [GeneratedRegex(@"hq_str_fx_shkdcny=""(?<data>[^""]*)""")]
    private static partial Regex FxLine();

    public static IReadOnlyList<Quote> ParseBody(string body)
    {
        var quotes = new List<Quote>();
        foreach (Match m in Line().Matches(body))
        {
            var data = m.Groups["data"].Value;
            if (data.Length == 0) continue;
            if (!SecurityId.TryParse(m.Groups["sym"].Value, out var id, out _)) continue;
            var l = id.IsHongKong ? HongKong : AShare;
            var f = data.Split(',');
            if (f.Length <= Math.Max(l.Date, l.Time)) continue;

            if (!decimal.TryParse(f[l.Price], NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                || !decimal.TryParse(f[l.PrevClose], NumberStyles.Number, CultureInfo.InvariantCulture, out var prev)
                || !DateTime.TryParseExact($"{f[l.Date]} {f[l.Time]}", l.TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new FormatException($"新浪行情格式无法解析：{m.Value}");

            if (Quote.Create(id, f[l.Name], TrimPrice(id, price), TrimPrice(id, prev), time) is { } q)
                quotes.Add(q);
        }
        return quotes;
    }

    /// <summary><c>var hq_str_fx_shkdcny="16:09:54,0.8510,...,0.8510,港元兑人民币,...";</c> Field 8 is the latest rate.</summary>
    public static decimal ParseFxBody(string body)
    {
        var m = FxLine().Match(body);
        var f = m.Success ? m.Groups["data"].Value.Split(',') : [];
        if (f.Length <= FxRateField
            || !decimal.TryParse(f[FxRateField], NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
            throw new FormatException($"新浪汇率格式无法解析：{body}");
        return CheckRate(Math.Round(rate, 4), m.Value);
    }

    private sealed record Layout(int Name, int PrevClose, int Price, int Date, int Time, string TimeFormat);
}

using System.Diagnostics.CodeAnalysis;

namespace KanPan.Core.Models;

public enum Market { SH, SZ, BJ, HK }

public static class MarketExtensions
{
    public static MarketGroup Group(this Market market) =>
        market == Market.HK ? MarketGroup.HongKong : MarketGroup.AShare;
}

public readonly record struct SecurityId(Market Market, string Code)
{
    public string Symbol => Market.ToString().ToLowerInvariant() + Code;

    public bool IsHongKong => Market == Market.HK;

    public MarketGroup Group => Market.Group();

    /// <summary>Prices and costs are quoted in HKD for Hong Kong, CNY otherwise.</summary>
    public string CurrencyUnit => IsHongKong ? "港元" : "元";

    public override string ToString() => Symbol;

    public static SecurityId Parse(string raw) =>
        TryParse(raw, out var id, out var error) ? id : throw new FormatException(error);

    /// <summary>
    /// Accepts "600519", "sh600519", "600519.SH", "00700", "700", "hk00700", "0700.HK".
    /// A bare 6-digit code is assigned to an exchange by its prefix; 1–5 digits are Hong Kong, padded to 5.
    /// </summary>
    public static bool TryParse(string? raw, out SecurityId id, [NotNullWhen(false)] out string? error)
    {
        id = default;
        var s = raw?.Trim().ToLowerInvariant() ?? "";
        if (s.Length == 0)
        {
            error = "代码为空";
            return false;
        }

        Market? explicitMarket = null;
        if (s.Length > 2 && TryMarketFromTag(s[..2], out var prefixMarket) && char.IsDigit(s[2]))
        {
            explicitMarket = prefixMarket;
            s = s[2..];
        }
        else if (s.Length > 3 && s[^3] == '.' && TryMarketFromTag(s[^2..], out var suffixMarket))
        {
            explicitMarket = suffixMarket;
            s = s[..^3];
        }

        if (!s.All(char.IsAsciiDigit))
        {
            error = $"无法识别的代码「{raw}」";
            return false;
        }

        if (explicitMarket == Market.HK || (explicitMarket is null && s.Length <= 5))
        {
            if (s.Length > 5 || s.TrimStart('0').Length == 0)
            {
                error = $"港股代码应为 1–5 位数字：「{raw}」";
                return false;
            }
            id = new SecurityId(Market.HK, s.PadLeft(5, '0'));
            error = null;
            return true;
        }

        if (s.Length != 6)
        {
            error = $"A 股代码应为 6 位数字：「{raw}」";
            return false;
        }

        var market = explicitMarket ?? InferAShareMarket(s);
        if (market is null)
        {
            error = $"无法判断「{raw}」属于哪个交易所，请加上 sh/sz/bj 前缀";
            return false;
        }

        id = new SecurityId(market.Value, s);
        error = null;
        return true;
    }

    private static Market? InferAShareMarket(string code) => code[0] switch
    {
        '6' or '5' => Market.SH,
        '0' or '3' or '2' => Market.SZ,
        '1' => code[1] == '1' ? Market.SH : Market.SZ,
        '4' or '8' => Market.BJ,
        '9' => code[1] == '2' ? Market.BJ : Market.SH,
        _ => null,
    };

    private static bool TryMarketFromTag(string tag, out Market market)
    {
        switch (tag)
        {
            case "sh": market = Market.SH; return true;
            case "sz": market = Market.SZ; return true;
            case "bj": market = Market.BJ; return true;
            case "hk": market = Market.HK; return true;
            default: market = default; return false;
        }
    }
}

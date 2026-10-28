using System.Globalization;
using System.Text.RegularExpressions;
using KanPan.Core.Models;

namespace KanPan.Core.Import;

public enum ParseStatus { Ok, Merged, Error }

public sealed record ParsedLine(int LineNumber, string Raw, Holding? Holding, ParseStatus Status, string? Message);

public sealed record ParseResult(IReadOnlyList<ParsedLine> Lines, IReadOnlyList<Holding> Holdings)
{
    public bool HasErrors => Lines.Any(l => l.Status == ParseStatus.Error);
}

/// <summary>
/// Parses lines of "code shares [cost]" separated by commas, whitespace, tabs or Chinese commas.
/// Blank lines, "#" comments and a leading header row are skipped. Duplicate codes are merged.
/// </summary>
public static partial class HoldingParser
{
    [GeneratedRegex(@"[,\s，;；]+")]
    private static partial Regex Separator();

    public static ParseResult Parse(string text)
    {
        var lines = new List<ParsedLine>();
        var order = new List<SecurityId>();
        var merged = new Dictionary<SecurityId, (long Shares, decimal? CostValue, bool CostMissing, int FirstIndex)>();
        var seenContent = false;

        var rawLines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < rawLines.Length; i++)
        {
            var raw = rawLines[i].Trim().TrimStart('\uFEFF');
            if (raw.Length == 0 || raw.StartsWith('#')) continue;

            if (!seenContent)
            {
                seenContent = true;
                if (IsHeader(raw)) continue;
            }

            var lineNo = i + 1;
            var parts = Separator().Split(raw).Where(p => p.Length > 0).ToArray();
            if (parts.Length < 2)
            {
                lines.Add(new ParsedLine(lineNo, raw, null, ParseStatus.Error, "缺少持仓股数"));
                continue;
            }

            if (!SecurityId.TryParse(parts[0], out var id, out var codeError))
            {
                lines.Add(new ParsedLine(lineNo, raw, null, ParseStatus.Error, codeError));
                continue;
            }

            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var shares) || shares <= 0)
            {
                lines.Add(new ParsedLine(lineNo, raw, null, ParseStatus.Error, $"股数应为正整数：「{parts[1]}」"));
                continue;
            }

            decimal? cost = null;
            if (parts.Length >= 3)
            {
                // Diluted cost can legitimately be zero or negative once the principal has been taken out.
                if (!decimal.TryParse(parts[2], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var c))
                {
                    lines.Add(new ParsedLine(lineNo, raw, null, ParseStatus.Error, $"成本价应为数字：「{parts[2]}」"));
                    continue;
                }
                cost = c;
            }

            var holding = new Holding(id, shares, cost);
            if (merged.TryGetValue(id, out var acc))
            {
                merged[id] = (
                    acc.Shares + shares,
                    cost is null ? acc.CostValue : (acc.CostValue ?? 0) + cost * shares,
                    acc.CostMissing || cost is null,
                    acc.FirstIndex);
                var note = acc.CostMissing || cost is null ? "，部分行缺少成本价，合并后不计成本" : "，成本价按股数加权平均";
                lines.Add(new ParsedLine(lineNo, raw, holding, ParseStatus.Merged, $"与第 {lines[acc.FirstIndex].LineNumber} 行重复，已合并股数{note}"));
            }
            else
            {
                merged[id] = (shares, cost * shares, cost is null, lines.Count);
                order.Add(id);
                lines.Add(new ParsedLine(lineNo, raw, holding, ParseStatus.Ok, null));
            }
        }

        var holdings = order.Select(id =>
        {
            var m = merged[id];
            decimal? cost = m.CostMissing || m.CostValue is null ? null : Math.Round(m.CostValue.Value / m.Shares, 4);
            return new Holding(id, m.Shares, cost);
        }).ToList();

        return new ParseResult(lines, holdings);
    }

    public static string Format(IEnumerable<Holding> holdings) =>
        string.Join(Environment.NewLine, holdings.Select(h =>
            h.CostPrice is { } c
                ? $"{h.Id.Symbol},{h.Shares},{c.ToString("0.####", CultureInfo.InvariantCulture)}"
                : $"{h.Id.Symbol},{h.Shares}"));

    private static bool IsHeader(string line)
    {
        var first = Separator().Split(line).FirstOrDefault() ?? "";
        return first.Contains("代码") || first.Contains("code", StringComparison.OrdinalIgnoreCase);
    }
}

using System.Globalization;

namespace KanPan.Core.Alerts;

public sealed record AlertMessage(string Title, string Body, string? Symbol);

public static class AlertMessages
{
    /// <summary>Above this many events in one round, they are merged into a single notification.</summary>
    public const int MaxIndividual = 3;

    /// <param name="isFirstRound">On the first refresh after launch, existing big moves are summarized instead of listed.</param>
    public static IReadOnlyList<AlertMessage> Compose(IReadOnlyList<AlertEvent> events, bool isFirstRound, decimal firstThreshold)
    {
        if (events.Count == 0) return [];

        if (events.Count == 1 || (!isFirstRound && events.Count <= MaxIndividual))
            return events.Select(Single).ToList();

        var ordered = events.OrderByDescending(e => Math.Abs(e.ChangePct)).ToList();
        var title = $"{events.Count} 只持仓涨跌幅超过 {FormatThreshold(firstThreshold)}%";
        var lines = ordered.Take(5).Select(e => $"{e.Name} {FormatPct(e.ChangePct)}");
        var body = string.Join("\n", lines) + (ordered.Count > 5 ? $"\n等 {ordered.Count} 只" : "");
        return [new AlertMessage(title, body, null)];
    }

    private static AlertMessage Single(AlertEvent e)
    {
        var tag = e.IsUp ? "大涨" : "大跌";
        var title = $"【{tag}】{e.Name} {FormatPct(e.ChangePct)}";
        var body = $"{e.Id.Code}  现价 {e.Price.ToString("0.00##", CultureInfo.InvariantCulture)}，已超过 {FormatThreshold(e.Threshold)}%";
        return new AlertMessage(title, body, e.Id.Symbol);
    }

    public static string FormatPct(decimal pct) =>
        (pct > 0 ? "+" : "") + pct.ToString("0.00", CultureInfo.InvariantCulture) + "%";

    private static string FormatThreshold(decimal t) => t.ToString("0.##", CultureInfo.InvariantCulture);
}

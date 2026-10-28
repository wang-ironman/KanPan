using KanPan.Core.Models;

namespace KanPan.Core.Alerts;

/// <param name="Level">1-based index of the threshold that was crossed.</param>
public sealed record AlertEvent(
    SecurityId Id,
    string Name,
    decimal ChangePct,
    decimal Price,
    int Level,
    decimal Threshold)
{
    public bool IsUp => ChangePct > 0;
}

/// <summary>Highest threshold level already alerted today, per symbol and direction. Persisted across restarts.</summary>
public sealed class AlertState
{
    public DateOnly TradeDate { get; set; }
    public Dictionary<string, AlertMark> Marks { get; set; } = new();
}

public sealed class AlertMark
{
    public int Up { get; set; }
    public int Down { get; set; }
}

public sealed class AlertEngine
{
    /// <summary>Gap below the first threshold a move must fall back to before it can alert again.</summary>
    public const decimal RetreatBuffer = 1m;

    private readonly decimal[] _levels;
    private readonly bool _realertOnRetreat;

    public AlertState State { get; private set; }

    /// <param name="levels">Absolute percentage thresholds, e.g. [5, 7, 9.5].</param>
    public AlertEngine(IEnumerable<decimal> levels, bool realertOnRetreat, AlertState? state = null)
    {
        _levels = levels.Where(l => l > 0).Distinct().Order().ToArray();
        if (_levels.Length == 0)
            throw new ArgumentException("至少需要一个提醒阈值", nameof(levels));
        _realertOnRetreat = realertOnRetreat;
        State = state ?? new AlertState();
    }

    public decimal FirstThreshold => _levels[0];

    /// <summary>
    /// Returns the alerts newly triggered by <paramref name="quotes"/>. Callers should only pass quotes
    /// from the current trading session during continuous trading.
    /// </summary>
    public IReadOnlyList<AlertEvent> Evaluate(IEnumerable<Quote> quotes, DateOnly tradeDate)
    {
        if (State.TradeDate != tradeDate)
            State = new AlertState { TradeDate = tradeDate };

        var events = new List<AlertEvent>();
        foreach (var q in quotes)
        {
            if (q.IsSuspended) continue;

            var pct = q.ChangePct;
            var abs = Math.Abs(pct);
            if (!State.Marks.TryGetValue(q.Id.Symbol, out var mark))
            {
                mark = new AlertMark();
                State.Marks[q.Id.Symbol] = mark;
            }

            if (_realertOnRetreat && abs < _levels[0] - RetreatBuffer)
            {
                mark.Up = 0;
                mark.Down = 0;
            }

            var level = LevelOf(abs);
            if (level == 0) continue;

            if (pct > 0 && level > mark.Up)
            {
                mark.Up = level;
                events.Add(new AlertEvent(q.Id, q.Name, pct, q.Price, level, _levels[level - 1]));
            }
            else if (pct < 0 && level > mark.Down)
            {
                mark.Down = level;
                events.Add(new AlertEvent(q.Id, q.Name, pct, q.Price, level, _levels[level - 1]));
            }
        }
        return events;
    }

    private int LevelOf(decimal absPct)
    {
        var level = 0;
        while (level < _levels.Length && absPct >= _levels[level]) level++;
        return level;
    }
}

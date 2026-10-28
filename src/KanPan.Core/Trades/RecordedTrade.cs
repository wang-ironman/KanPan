namespace KanPan.Core.Trades;

/// <summary>A local execution, dated in Beijing time, used to retain realized day P&amp;L.</summary>
public sealed record RecordedTrade(DateOnly Date, Trade Trade);

namespace KanPan.Core.Models;

/// <summary><see cref="ClosingAuction"/> only exists in Hong Kong (16:00–16:10).</summary>
public enum MarketPhase { Closed, PreOpen, Trading, Lunch, ClosingAuction }

/// <summary>Markets that share a trading calendar: SH/SZ/BJ trade together, Hong Kong on its own hours.</summary>
public enum MarketGroup { AShare, HongKong }

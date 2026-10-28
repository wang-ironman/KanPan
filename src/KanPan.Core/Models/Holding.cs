namespace KanPan.Core.Models;

public sealed record Holding(SecurityId Id, long Shares, decimal? CostPrice);

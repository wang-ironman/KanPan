using KanPan.Core.Models;
using KanPan.Core.Quotes;

namespace KanPan.Tests;

public sealed class FakeTime(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public static FakeTime AtBeijing(DateTime beijing) =>
        new(new DateTimeOffset(beijing, TimeSpan.FromHours(8)));

    public void SetBeijing(DateTime beijing) => UtcNow = new DateTimeOffset(beijing, TimeSpan.FromHours(8));

    public void Advance(TimeSpan by) => UtcNow += by;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

public sealed class FakeFxProvider(decimal rate) : IFxRateProvider
{
    public string Name => "fake-fx";
    public decimal Rate { get; set; } = rate;
    public int Calls { get; private set; }
    public Exception? FailWith { get; set; }

    public Task<decimal> GetHkdCnyAsync(CancellationToken ct)
    {
        Calls++;
        if (FailWith is not null) throw FailWith;
        return Task.FromResult(Rate);
    }
}

public sealed class FakeQuoteProvider(string name = "fake") : IQuoteProvider
{
    public string Name => name;
    public int Calls { get; private set; }
    public Exception? FailWith { get; set; }
    public Func<IReadOnlyList<SecurityId>, IReadOnlyList<Quote>> Respond { get; set; } = _ => [];
    public List<IReadOnlyList<SecurityId>> Requests { get; } = [];

    public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<SecurityId> ids, CancellationToken ct)
    {
        Calls++;
        Requests.Add(ids);
        if (FailWith is not null) throw FailWith;
        return Task.FromResult(Respond(ids));
    }
}

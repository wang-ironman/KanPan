using KanPan.Core.Models;
using KanPan.Infrastructure.Quotes;

namespace KanPan.Tests;

public class FailoverQuoteProviderTests
{
    private static readonly IReadOnlyList<SecurityId> Ids = [SecurityId.Parse("600519")];

    private readonly FakeQuoteProvider _primary = new("primary");
    private readonly FakeQuoteProvider _backup = new("backup");
    private readonly FakeTime _time = new(DateTimeOffset.UnixEpoch);
    private readonly FailoverQuoteProvider _sut;

    public FailoverQuoteProviderTests() => _sut = new FailoverQuoteProvider(_primary, _backup, _time);

    [Fact]
    public async Task Switches_to_backup_on_third_consecutive_failure()
    {
        _primary.FailWith = new HttpRequestException("down");

        await Assert.ThrowsAsync<HttpRequestException>(() => _sut.GetQuotesAsync(Ids, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => _sut.GetQuotesAsync(Ids, default));
        await _sut.GetQuotesAsync(Ids, default);

        Assert.True(_sut.UsingBackup);
        Assert.Equal("backup", _sut.Name);
        Assert.Equal(1, _backup.Calls);
    }

    [Fact]
    public async Task Success_resets_failure_count()
    {
        _primary.FailWith = new HttpRequestException("down");
        await Assert.ThrowsAsync<HttpRequestException>(() => _sut.GetQuotesAsync(Ids, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => _sut.GetQuotesAsync(Ids, default));
        _primary.FailWith = null;
        await _sut.GetQuotesAsync(Ids, default);
        _primary.FailWith = new HttpRequestException("down");
        await Assert.ThrowsAsync<HttpRequestException>(() => _sut.GetQuotesAsync(Ids, default));

        Assert.False(_sut.UsingBackup);
        Assert.Equal(0, _backup.Calls);
    }

    [Fact]
    public async Task Probes_primary_only_after_interval_and_switches_back()
    {
        _primary.FailWith = new HttpRequestException("down");
        for (var i = 0; i < 3; i++)
            try { await _sut.GetQuotesAsync(Ids, default); } catch (HttpRequestException) { }
        var primaryCalls = _primary.Calls;

        _time.Advance(TimeSpan.FromMinutes(1));
        await _sut.GetQuotesAsync(Ids, default);
        Assert.Equal(primaryCalls, _primary.Calls);

        _primary.FailWith = null;
        _time.Advance(FailoverQuoteProvider.ProbeInterval);
        await _sut.GetQuotesAsync(Ids, default);
        Assert.Equal(primaryCalls + 1, _primary.Calls);
        Assert.False(_sut.UsingBackup);
    }
}

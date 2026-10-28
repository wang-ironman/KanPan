using KanPan.Core.Schedule;

namespace KanPan.Tests;

public class AutoExitPolicyTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 21);
    private static DateTime At(DateOnly day, int h, int m) => day.ToDateTime(new TimeOnly(h, m));

    private readonly AutoExitPolicy _sut = new(new TimeOnly(15, 10));

    [Fact]
    public void Exits_when_running_through_exit_time()
    {
        Assert.False(_sut.ShouldExit(At(Wednesday, 9, 0)));
        Assert.False(_sut.ShouldExit(At(Wednesday, 15, 9)));
        Assert.True(_sut.ShouldExit(At(Wednesday, 15, 10)));
    }

    [Fact]
    public void Started_after_exit_time_keeps_running() =>
        Assert.False(_sut.ShouldExit(At(Wednesday, 20, 0)));

    [Fact]
    public void Sleeping_across_exit_time_still_exits()
    {
        _sut.ShouldExit(At(Wednesday, 14, 0));
        Assert.True(_sut.ShouldExit(At(Wednesday, 18, 30)));
    }

    [Fact]
    public void Evening_start_running_overnight_exits_next_afternoon()
    {
        Assert.False(_sut.ShouldExit(At(Wednesday, 20, 0)));
        var thursday = Wednesday.AddDays(1);
        Assert.False(_sut.ShouldExit(At(thursday, 9, 0)));
        Assert.True(_sut.ShouldExit(At(thursday, 15, 10)));
    }

    [Fact]
    public void Sleeping_overnight_past_next_exit_time_does_not_exit()
    {
        _sut.ShouldExit(At(Wednesday, 20, 0));
        Assert.False(_sut.ShouldExit(At(Wednesday.AddDays(1), 16, 0)));
    }

    [Theory]
    [InlineData(15, 10, false, 15, 10)]
    [InlineData(15, 10, true, 16, 20)]
    [InlineData(17, 0, true, 17, 0)]
    public void Hong_kong_holdings_push_the_exit_past_the_hong_kong_close(int h, int m, bool hk, int eh, int em) =>
        Assert.Equal(new TimeOnly(eh, em), AutoExitPolicy.Effective(new TimeOnly(h, m), hk));

    [Fact]
    public void Weekend_never_exits()
    {
        var saturday = new DateOnly(2026, 10, 24);
        _sut.ShouldExit(At(saturday, 10, 0));
        Assert.False(_sut.ShouldExit(At(saturday, 15, 30)));
    }
}

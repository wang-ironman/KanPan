namespace KanPan.Core.Schedule;

/// <summary>
/// Decides when to quit after the close. The app only quits on a weekday it was already running
/// before <see cref="ExitTime"/>, so opening it in the evening to review the day does not close it again.
/// Call <see cref="ShouldExit"/> periodically with Beijing time; a gap (e.g. sleep) across the exit time still triggers.
/// </summary>
public sealed class AutoExitPolicy(TimeOnly exitTime)
{
    /// <summary>Ten minutes after the Hong Kong closing auction ends (16:10).</summary>
    public static readonly TimeOnly HongKongExitTime = new(16, 20);

    private DateOnly? _armedOn;

    public TimeOnly ExitTime { get; } = exitTime;

    /// <summary>The configured time, pushed back to <see cref="HongKongExitTime"/> while Hong Kong stocks are held.</summary>
    public static TimeOnly Effective(TimeOnly configured, bool holdsHongKong) =>
        holdsHongKong && configured < HongKongExitTime ? HongKongExitTime : configured;

    public bool ShouldExit(DateTime beijingNow)
    {
        var today = DateOnly.FromDateTime(beijingNow);
        if (beijingNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        if (TimeOnly.FromDateTime(beijingNow) < ExitTime)
        {
            _armedOn = today;
            return false;
        }
        return _armedOn == today;
    }
}

namespace KanPan.Core.Schedule;

public static class BeijingTime
{
    // China has not observed daylight saving time since 1991, so a fixed offset is exact.
    private static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    public static DateTime Now(TimeProvider time) => time.GetUtcNow().UtcDateTime + Offset;
}

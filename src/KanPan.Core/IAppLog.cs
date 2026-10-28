namespace KanPan.Core;

public interface IAppLog
{
    void Info(string message);
    void Error(string message, Exception? ex = null);
}

public sealed class NullLog : IAppLog
{
    public static readonly NullLog Instance = new();
    public void Info(string message) { }
    public void Error(string message, Exception? ex = null) { }
}

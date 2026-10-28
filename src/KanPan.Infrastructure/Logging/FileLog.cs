using KanPan.Core;

namespace KanPan.Infrastructure.Logging;

/// <summary>Daily log files (kanpan-yyyyMMdd.log), keeping the last <see cref="RetentionDays"/> days.</summary>
public sealed class FileLog : IAppLog
{
    public const int RetentionDays = 7;

    private readonly string _directory;
    private readonly Lock _lock = new();

    public FileLog(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        PurgeOld();
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        var now = DateTime.Now;
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (ex is not null) line += Environment.NewLine + ex;
        lock (_lock)
        {
            try
            {
                File.AppendAllText(Path.Combine(_directory, $"kanpan-{now:yyyyMMdd}.log"), line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }

    private void PurgeOld()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "kanpan-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch (IOException) { }
        }
    }
}

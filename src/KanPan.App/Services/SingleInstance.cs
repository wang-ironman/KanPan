namespace KanPan.App.Services;

/// <summary>
/// Owns a named mutex for the session. A second launch signals the first one (so it can show its window) and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, Action onActivated)
    {
        _mutex = mutex;
        _activate = activate;
        _registration = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => onActivated(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Returns null when another instance already runs; that instance has been asked to activate.</summary>
    public static SingleInstance? TryAcquire(Action onActivated, string scope = "KanPan")
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{scope}.SingleInstance", out var createdNew);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{scope}.Activate");
        if (!createdNew)
        {
            activate.Set();
            activate.Dispose();
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, activate, onActivated);
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}

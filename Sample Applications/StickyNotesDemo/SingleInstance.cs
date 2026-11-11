using System;
using System.Threading;

namespace StickyNotesDemo;

// Acquired before reading or writing note data. Shared by all copies of the app.
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle listener;
    public bool IsPrimary { get; }

    public SingleInstance(string name)
    {
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        mutex = new Mutex(false, name + ".Mutex");
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (!IsPrimary) activation.Set();
    }

    public void Listen(Action onActivate) => listener = ThreadPool.RegisterWaitForSingleObject(
        activation, (_, _) => onActivate(), null, Timeout.Infinite, false);

    public void Dispose()
    {
        listener?.Unregister(null);
        if (IsPrimary) mutex.ReleaseMutex();
        activation.Dispose();
        mutex.Dispose();
    }
}

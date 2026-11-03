namespace NjcAI.Monitor.Core;

internal sealed class SingleInstanceGuard : IDisposable
{
    internal const string ApplicationMutexName = @"Local\NjcAI.Monitor.SingleInstance";

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public bool HasOwnership => _ownsMutex;

    public static SingleInstanceGuard TryAcquire(string? mutexName = null)
    {
        var mutex = new Mutex(true, mutexName ?? ApplicationMutexName, out var createdNew);
        return new SingleInstanceGuard(mutex, createdNew);
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The owning thread is already shutting down; closing the handle is sufficient.
            }
            _ownsMutex = false;
        }
        _mutex.Dispose();
    }
}

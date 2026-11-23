namespace Beam.Core.Startup;

/// <summary>
/// Short-lived per-session lock shared by tray and CLI display mutations. This is deliberately
/// separate from <see cref="SingleInstanceLock"/>: the tray owns its singleton mutex for life,
/// while either process may perform a serialized display operation at any time.
/// </summary>
public static class DisplayOperationLock
{
    /// <summary>The named semaphore shared by every Beam process in this user session.</summary>
    public const string SemaphoreName = @"Local\Beam.DisplayOperation";

    /// <summary>How long one command waits for another process's operation.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Tries to acquire the operation semaphore. A semaphore has no thread affinity, so the
    /// returned lease can safely be disposed after an async operation resumes on another thread.
    /// Returns null on timeout or an unusable name; never throws.
    /// </summary>
    public static IDisposable? TryAcquire(TimeSpan? timeout = null, string? semaphoreName = null)
    {
        Semaphore? semaphore = null;
        try
        {
            semaphore = new Semaphore(initialCount: 1, maximumCount: 1, semaphoreName ?? SemaphoreName);
            if (!semaphore.WaitOne(timeout ?? DefaultTimeout))
            {
                semaphore.Dispose();
                return null;
            }

            return new Lease(semaphore);
        }
        catch
        {
            semaphore?.Dispose();
            return null;
        }
    }

    private sealed class Lease : IDisposable
    {
        private Semaphore? _semaphore;

        public Lease(Semaphore semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            Semaphore? semaphore = Interlocked.Exchange(ref _semaphore, null);
            if (semaphore is null)
            {
                return;
            }

            try
            {
                _ = semaphore.Release();
            }
            catch
            {
                // Best effort during process teardown; the OS releases abandoned handles.
            }

            semaphore.Dispose();
        }
    }
}

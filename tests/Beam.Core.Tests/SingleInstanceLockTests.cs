using Beam.Core.Startup;

namespace Beam.Core.Tests;

/// <summary>
/// Tests for the tray's lifetime single-instance mutex. Cross-process display operations
/// use DisplayOperationLock instead. Tests always use a unique mutex name so parallel runs
/// never contend on the production Local\ lock.
/// </summary>
public class SingleInstanceLockTests
{
    private static string UniqueName() => "Beam.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void MutexName_IsTheSharedLocalSessionName()
    {
        // Both composition roots must agree on this exact name (A1).
        Assert.Equal(@"Local\Beam.SingleInstance", SingleInstanceLock.MutexName);
    }

    [Fact]
    public void TryAcquire_ReturnsLease_WhenFree()
    {
        using IDisposable? lease = SingleInstanceLock.TryAcquire(mutexName: UniqueName());

        Assert.NotNull(lease);
    }

    [Fact]
    public void TryAcquire_ReturnsNull_WhenHeld_AndSucceedsAgainAfterRelease()
    {
        string name = UniqueName();

        // A mutex is recursive for its owning thread, so contention can only be
        // observed from a DIFFERENT thread: hold the lock on a worker thread while
        // the test thread tries (and fails) to acquire it.
        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            try
            {
                using var m = new Mutex(initiallyOwned: false, name);
                m.WaitOne();
                held.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                m.ReleaseMutex();
            }
            catch (Exception ex)
            {
                workerError = ex;
            }
        });
        worker.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(10)), "worker never acquired the mutex");

        using IDisposable? second = SingleInstanceLock.TryAcquire(TimeSpan.FromMilliseconds(200), name);
        Assert.Null(second);

        release.Set();
        worker.Join();
        Assert.Null(workerError);

        using IDisposable? third = SingleInstanceLock.TryAcquire(TimeSpan.FromSeconds(2), name);
        Assert.NotNull(third);
    }

    [Fact]
    public void TryAcquire_TreatsAbandonedMutex_AsAcquired()
    {
        string name = UniqueName();

        // A thread that acquires the mutex and exits without releasing abandons it;
        // the next waiter gets AbandonedMutexException but still OWNS the mutex.
        var t = new Thread(() =>
        {
            var m = new Mutex(initiallyOwned: false, name);
            m.WaitOne();
            // no ReleaseMutex — thread ends, mutex becomes abandoned
        });
        t.Start();
        t.Join();

        using IDisposable? lease = SingleInstanceLock.TryAcquire(TimeSpan.FromSeconds(2), name);
        Assert.NotNull(lease);
    }

    [Fact]
    public void Lease_Dispose_IsIdempotent()
    {
        IDisposable? lease = SingleInstanceLock.TryAcquire(mutexName: UniqueName());
        Assert.NotNull(lease);

        lease!.Dispose();
        lease.Dispose(); // must not throw
    }
}

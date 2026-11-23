using Beam.Core.Startup;

namespace Beam.Core.Tests;

public class DisplayOperationLockTests
{
    private static string UniqueName() => "Beam.Tests.Operation." + Guid.NewGuid().ToString("N");

    [Fact]
    public void SemaphoreName_IsSeparateFromTraySingleton()
    {
        Assert.Equal(@"Local\Beam.DisplayOperation", DisplayOperationLock.SemaphoreName);
        Assert.NotEqual(SingleInstanceLock.MutexName, DisplayOperationLock.SemaphoreName);
    }

    [Fact]
    public void HeldSemaphore_TimesOut_ThenCanBeAcquiredAfterRelease()
    {
        string name = UniqueName();
        using IDisposable? first = DisplayOperationLock.TryAcquire(semaphoreName: name);
        Assert.NotNull(first);

        Assert.Null(DisplayOperationLock.TryAcquire(TimeSpan.FromMilliseconds(50), name));

        first.Dispose();
        using IDisposable? second = DisplayOperationLock.TryAcquire(TimeSpan.FromSeconds(1), name);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task Lease_CanBeReleasedOnDifferentThread()
    {
        string name = UniqueName();
        IDisposable? lease = DisplayOperationLock.TryAcquire(semaphoreName: name);
        Assert.NotNull(lease);

        await Task.Run(() => lease.Dispose());

        using IDisposable? next = DisplayOperationLock.TryAcquire(TimeSpan.FromSeconds(1), name);
        Assert.NotNull(next);
        lease.Dispose(); // idempotent after cross-thread release
    }
}

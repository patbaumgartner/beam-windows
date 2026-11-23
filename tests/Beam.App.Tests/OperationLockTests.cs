using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

public class OperationLockTests
{
    [Fact]
    public async Task Operations_AcquireAndReleaseSharedLock_UnavailableRestoreStaysRetryable()
    {
        bool available = true;
        int attempts = 0;
        int acquired = 0;
        int released = 0;
        using var h = new PresentationHarness(
            Topologies.MirroredWithMismatch(),
            DpiScale.From(-4, 0, 4),
            operationLockFactory: () =>
            {
                attempts++;
                if (!available)
                {
                    return null;
                }

                acquired++;
                return new Lease(() => released++);
            });

        await h.Manager.ApplyNowAsync();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, attempts);
        Assert.Equal(1, acquired);
        Assert.Equal(1, released);

        available = false;
        await h.Manager.RestoreNowAsync();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(0, h.Mode.RestoreCalls);
        Assert.Equal(2, attempts);
        Assert.Equal(1, acquired);
        Assert.Equal(1, released);

        available = true;
        await h.Manager.RestoreNowAsync();
        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Equal(3, attempts);
        Assert.Equal(2, acquired);
        Assert.Equal(2, released);
    }

    private sealed class Lease : IDisposable
    {
        private readonly Action _onDispose;
        public Lease(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}

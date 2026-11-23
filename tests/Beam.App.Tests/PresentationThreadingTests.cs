using Beam.Core.Display;

namespace Beam.App.Tests;

public class PresentationThreadingTests
{
    [Fact]
    public async Task PublicOperations_NeverRunNativePortsOnCallerThread()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            using var h = new PresentationHarness(
                Topologies.MirroredWithMismatch(), DpiScale.From(-4, 0, 4));
            int callerThread = Environment.CurrentManagedThreadId;
            var portThreads = new List<int>();
            h.Mode.BeforeCall = () => portThreads.Add(Environment.CurrentManagedThreadId);
            h.NightLight.BeforeCall = () => portThreads.Add(Environment.CurrentManagedThreadId);

            await h.Manager.ApplyNowAsync();
            await h.Manager.RestoreNowAsync();
            await h.Manager.CheckCurrentStateAsync();
            await h.Manager.SetEnabledAsync(false);
            await h.Manager.SetEnabledAsync(true);
            await h.Manager.SetNightLightAsync(true);

            Assert.NotEmpty(portThreads);
            Assert.All(portThreads, thread => Assert.NotEqual(callerThread, thread));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}

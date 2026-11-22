using Beam.App.Sessions;
using Beam.Core.Display;
using Beam.Core.NightLight;

namespace Beam.App.Tests;

/// <summary>
/// Returned (non-throwing) restore failures must stay visible and retryable. Clearing the
/// journal or returning to Idle would lose the original settings and arm a destructive reapply.
/// </summary>
public class RestoreFailureTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    [Fact]
    public async Task ModeRestoreFailure_KeepsActiveJournal_AndRetryCanComplete()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Mode.RestoreResult = "driver rejected restore";

        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.True(h.Journal.Record!.IsActive);
        Assert.Equal(0, h.Journal.ClearCalls);

        h.Mode.RestoreResult = null;
        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Null(h.Journal.Record);
        Assert.Equal(1, h.Journal.ClearCalls);
        Assert.Equal(2, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task NightLightRestoreFailure_KeepsActiveJournal_AndRetryCanComplete()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.NightLight.Snapshots = new[]
        {
            new NightLightSnapshot(@"KEY\default", "Data", Array.Empty<byte>(), null!),
        };
        await h.Manager.ApplyNowAsync();
        h.NightLight.WriteResult = false;

        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.True(h.Journal.Record!.IsActive);
        Assert.Equal(0, h.Journal.ClearCalls);

        h.NightLight.WriteResult = true;
        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Null(h.Journal.Record);
        Assert.Equal(2, h.NightLight.RestoreCalls);
    }

    [Fact]
    public async Task DpiRestoreFailure_KeepsActiveJournal_AndRetryCanComplete()
    {
        using var h = new PresentationHarness(Topologies.MirroredNeedScale(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Dpi.SetResult = false;

        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.True(h.Journal.Record!.IsActive);
        Assert.Equal(0, h.Journal.ClearCalls);

        h.Dpi.SetResult = true;
        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Null(h.Journal.Record);
        Assert.Equal(1, h.Journal.ClearCalls);
    }
}

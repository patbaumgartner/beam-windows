using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

/// <summary>
/// UC-12 guardrail tests: verify-after-mode, safe mode after consecutive failures,
/// and self-change suppression. All use the fake ports plus an injectable clock so the
/// 3 s self-change window is exercised deterministically.
/// </summary>
public class GuardrailTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset Read() => Now;
        public void Advance(TimeSpan by) => Now = Now.Add(by);
    }

    [Fact]
    public async Task VerifyAfterMode_TriggerStillMet_UnwindsToIdle()
    {
        using var h = new PresentationHarness(
            Topologies.MirroredWithMismatch(),
            ScaleAt200,
            verifySettleInterval: TimeSpan.FromMilliseconds(1),
            verifyMaxAttempts: 5);
        int queriesAtApply = -1;
        // Apply "succeeds" but the topology never actually changes; record how many
        // topology queries happened up to the apply so the settle-poll is measurable.
        h.Mode.OnApply = () => queriesAtApply = h.Topology.QueryCount;

        await h.Manager.ApplyNowAsync();

        // UC-12 rule 4: the fix is rejected and unwound via restore, not left Active.
        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.False(h.Manager.IsSafeMode); // a single failure is below the threshold

        // Issue #1: the verify must poll (bounded) instead of reading once. With
        // verifyMaxAttempts 5 the settle loop re-queries after the apply; a single-shot
        // verify would read the post-apply topology exactly once.
        Assert.True(h.Topology.QueryCount - queriesAtApply >= 5,
            $"expected the settle-poll to re-query after apply, QueryCount={h.Topology.QueryCount}, atApply={queriesAtApply}");
    }

    [Fact]
    public async Task VerifyAfterMode_StaleReadsThenSettles_SucceedsWithoutUnwind()
    {
        using var h = new PresentationHarness(
            Topologies.MirroredWithMismatch(),
            ScaleAt200,
            verifySettleInterval: TimeSpan.FromMilliseconds(1),
            verifyMaxAttempts: 10);
        int queriesAtApply = -1;
        h.Mode.OnApply = () => queriesAtApply = h.Topology.QueryCount; // apply "succeeds"...
        h.Topology.BeforeCall = () =>
        {
            // ...but the OS has not re-enumerated yet: the first two post-apply reads
            // are still stale, the third reports the fresh 16:9 clone.
            if (queriesAtApply >= 0 && h.Topology.QueryCount - queriesAtApply >= 3)
            {
                h.Topology.Topology = Topologies.CloneFixed16x9();
            }
        };

        await h.Manager.ApplyNowAsync();

        // Issue #1: the bounded settle-poll observes the fresh topology, so the fix
        // succeeds instead of false-failing on the stale post-apply read.
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(0, h.Mode.RestoreCalls);
        Assert.True(h.Topology.QueryCount - queriesAtApply >= 4,
            $"expected settle re-queries after apply, QueryCount={h.Topology.QueryCount}, atApply={queriesAtApply}");
    }

    [Fact]
    public async Task SafeMode_EnteredAfterTwoConsecutiveFailures_EventFiresOnce()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Mode.ApplyResult = "mode rejected";
        var events = new List<bool>();
        h.Manager.SafeModeChanged += safe => events.Add(safe);

        await h.Manager.ApplyNowAsync();
        Assert.False(h.Manager.IsSafeMode);
        Assert.Empty(events);

        await h.Manager.ApplyNowAsync();
        Assert.True(h.Manager.IsSafeMode);
        Assert.Equal(new[] { true }, events);
        Assert.Equal(SessionState.Idle, h.Session.State);
    }

    [Fact]
    public async Task SafeMode_BlocksAutoApply_ButNotManualApplyWhichClearsIt()
    {
        var clock = new FakeClock();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, clock: clock.Read);
        h.Mode.ApplyResult = "mode rejected";

        await h.Manager.ApplyNowAsync();
        await h.Manager.ApplyNowAsync();
        Assert.True(h.Manager.IsSafeMode);
        int applyCalls = h.Mode.ApplyCalls;

        // Automatic path (display change with the trigger met) is blocked in safe mode.
        // Advance the clock past the self-change window so only safe mode can be the cause.
        clock.Advance(TimeSpan.FromSeconds(10));
        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask;

        Assert.Equal(applyCalls, h.Mode.ApplyCalls);
        Assert.Equal(SessionState.Idle, h.Session.State);

        // Manual ApplyNow is the escape hatch; a success clears safe mode and fires the event.
        h.Mode.ApplyResult = null;
        var events = new List<bool>();
        h.Manager.SafeModeChanged += safe => events.Add(safe);

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.False(h.Manager.IsSafeMode);
        Assert.Equal(new[] { false }, events);
    }

    [Fact]
    public async Task SafeMode_BlocksStartupCheck_Too()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Mode.ApplyResult = "mode rejected";
        await h.Manager.ApplyNowAsync();
        await h.Manager.ApplyNowAsync();
        Assert.True(h.Manager.IsSafeMode);

        await h.Manager.CheckCurrentStateAsync();

        Assert.Equal(2, h.Mode.ApplyCalls); // unchanged — no auto apply
        Assert.Equal(SessionState.Idle, h.Session.State);
    }

    [Fact]
    public async Task ClearSafeMode_AllowsAutoApplyAgain()
    {
        var clock = new FakeClock();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, clock: clock.Read);
        h.Mode.ApplyResult = "mode rejected";
        var events = new List<bool>();
        h.Manager.SafeModeChanged += safe => events.Add(safe);

        await h.Manager.ApplyNowAsync();
        await h.Manager.ApplyNowAsync();
        Assert.True(h.Manager.IsSafeMode);
        Assert.Equal(new[] { true }, events);

        h.Manager.ClearSafeMode();
        Assert.False(h.Manager.IsSafeMode);
        Assert.Equal(new[] { true, false }, events);

        clock.Advance(TimeSpan.FromSeconds(10)); // past the self-change window
        h.Mode.ApplyResult = null;
        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask;

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(3, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task SelfChangeWithinWindow_SuppressesAutoFix_WindowExpiry_AllowsIt()
    {
        var clock = new FakeClock();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, clock: clock.Read);

        await h.Manager.ApplyNowAsync();   // Active; stamps own-mutation at clock.Now
        await h.Manager.RestoreNowAsync(); // Idle; stamps again at clock.Now
        h.Topology.Topology = Topologies.MirroredWithMismatch(); // bars are back

        // A change arriving inside the 3 s self-change window must not re-trigger.
        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask;

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);

        // After the window expires the same change applies normally.
        clock.Advance(PresentationManager.DefaultSelfChangeWindow + TimeSpan.FromSeconds(1));
        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask;

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(2, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task DisconnectRestore_WithinSelfChangeWindow_StillRestores()
    {
        var clock = new FakeClock();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, clock: clock.Read);

        await h.Manager.ApplyNowAsync();
        Assert.Equal(SessionState.Active, h.Session.State);

        // The disconnect arrives inside our own self-change window (clock not advanced).
        // The safety restore path must never be suppressed.
        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1));
        await h.Manager.PendingTask;

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
    }
}

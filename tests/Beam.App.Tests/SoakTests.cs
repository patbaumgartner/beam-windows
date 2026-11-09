using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

/// <summary>
/// Soak tests — the long-running acceptance scenarios from the spec:
/// G3 / UC-12 AC1-AC2 / S6 (500 automated apply/restore cycles, 0 black screens,
/// 500/500 valid end states), the UC-12 random-failure property at the manager level,
/// the roadmap Phase-1 exit criteria (unplug restore 20/20; kill -9 at 20 points → the
/// next start is always valid), and S5 (at most one apply per attach).
/// Everything is deterministic: fakes, seeded RNG, and polling helpers — no bare sleeps.
/// </summary>
public class SoakTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }

    // ── G3 / UC-12 AC1-AC2 / S6: 500 apply/restore cycles ───────────────────────

    [Fact]
    public async Task Soak_500ApplyRestoreCycles_ZeroBlackScreens_500ValidEndStates()
    {
        // Every cycle must end in a valid state: after apply, Active with the mismatch
        // fixed and the journal armed; after restore, Idle with the journal cleared.
        // A "black screen" in this simulated world is a half-applied state (Active while
        // the trigger is still met), which the per-cycle assertions would catch.
        const int cycles = 500;
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        int blackScreens = 0;
        int invalidEndStates = 0;

        for (int i = 0; i < cycles; i++)
        {
            await h.Manager.ApplyNowAsync();
            bool applyValid = h.Session.State == SessionState.Active
                && !PresentationManager.IsTriggerConditionMet(h.Topology.Topology)
                && h.Journal.Record is { IsActive: true };
            if (!applyValid)
            {
                invalidEndStates++;
            }

            if (h.Session.State == SessionState.Active
                && PresentationManager.IsTriggerConditionMet(h.Topology.Topology))
            {
                blackScreens++; // half-applied: fix claims Active but bars are still there
            }

            await h.Manager.RestoreNowAsync();
            bool restoreValid = h.Session.State == SessionState.Idle
                && h.Journal.Record is null;
            if (!restoreValid)
            {
                invalidEndStates++;
            }

            h.Topology.Topology = Topologies.MirroredWithMismatch(); // back to the original arrangement
        }

        Assert.Equal(0, blackScreens);
        Assert.Equal(0, invalidEndStates);
        Assert.Equal(cycles, h.Mode.ApplyCalls);
        Assert.Equal(cycles, h.Mode.RestoreCalls);
        Assert.Equal(cycles, h.Journal.ClearCalls);
    }

    // ── UC-12 property at the manager level: random failures ────────────────────

    [Fact]
    public async Task Soak_RandomFaultInjection_EndStateAlwaysValid_SafeModeTracksStreak()
    {
        // For a seeded-random pattern of injected apply failures, the end state after
        // every cycle is always valid — Active with the trigger gone, or Idle with the
        // journal cleared — never wedged in Applying/Restoring. Safe mode engages at
        // exactly MaxConsecutiveFailures and clears on the next success (UC-12 rule 11).
        var rng = new Random(0x5EED);
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        var safeModeEvents = new List<bool>();
        h.Manager.SafeModeChanged += s => safeModeEvents.Add(s);

        var expectedSafeModeEvents = new List<bool>();
        int consecutiveFailures = 0;
        int failedApplies = 0;

        const int trials = 300;
        for (int i = 0; i < trials; i++)
        {
            bool failApply = rng.Next(100) < 30;
            h.Mode.ApplyResult = failApply ? $"injected failure #{i}" : null;

            await h.Manager.ApplyNowAsync();

            if (failApply)
            {
                failedApplies++;
                consecutiveFailures++;
                // Failed fix unwinds completely: Idle, journal cleared, never half-applied.
                Assert.Equal(SessionState.Idle, h.Session.State);
                Assert.Null(h.Journal.Record);
                Assert.Equal(consecutiveFailures >= PresentationManager.MaxConsecutiveFailures, h.Manager.IsSafeMode);
                if (consecutiveFailures >= PresentationManager.MaxConsecutiveFailures)
                {
                    expectedSafeModeEvents.Add(true);
                }
            }
            else
            {
                if (consecutiveFailures >= PresentationManager.MaxConsecutiveFailures)
                {
                    expectedSafeModeEvents.Add(false); // leaving safe mode
                }

                consecutiveFailures = 0;
                Assert.Equal(SessionState.Active, h.Session.State);
                Assert.False(PresentationManager.IsTriggerConditionMet(h.Topology.Topology));
                Assert.NotNull(h.Journal.Record);
                Assert.False(h.Manager.IsSafeMode);
            }

            h.Mode.ApplyResult = null;
            h.Topology.Topology = Topologies.MirroredWithMismatch();
            await h.Manager.RestoreNowAsync();
            Assert.Equal(SessionState.Idle, h.Session.State);
            Assert.Null(h.Journal.Record);
        }

        Assert.Equal(trials, h.Mode.ApplyCalls);
        Assert.Equal(trials, h.Mode.RestoreCalls); // one unwind per failed apply + one manual per success
        Assert.Equal(expectedSafeModeEvents, safeModeEvents);
    }

    // ── Roadmap Phase-1 exit: unplug restore 20/20 ──────────────────────────────

    [Fact]
    public async Task Soak_UnplugWhileActive_Restores20OutOf20()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        const int cycles = 20;
        for (int i = 0; i < cycles; i++)
        {
            await h.Manager.ApplyNowAsync();
            Assert.Equal(SessionState.Active, h.Session.State);

            h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1)); // unplug
            await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(SessionState.Idle, h.Session.State);
            Assert.Null(h.Journal.Record);
            h.Topology.Topology = Topologies.MirroredWithMismatch(); // replug, mismatch again
        }

        Assert.Equal(cycles, h.Mode.ApplyCalls);
        Assert.Equal(cycles, h.Mode.RestoreCalls); // every unplug produced exactly one restore
    }

    // ── S5: at most one apply per attach ────────────────────────────────────────

    [Fact]
    public async Task Soak_AttachWithTrigger_AtMostOneApplyPerAttach()
    {
        var now = DateTimeOffset.UtcNow;
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200, clock: () => now);

        const int attaches = 10;
        for (int i = 0; i < attaches; i++)
        {
            h.Topology.Topology = Topologies.MirroredWithMismatch();
            h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2)); // plug in
            await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(SessionState.Active, h.Session.State);
            Assert.Equal(i + 1, h.Mode.ApplyCalls); // exactly one apply per attach

            // Noise: a repeated event for the same physical state must not re-apply.
            h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(2));
            await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(i + 1, h.Mode.ApplyCalls);

            h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1)); // unplug → restore
            await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(SessionState.Idle, h.Session.State);

            now = now.AddSeconds(4); // past the 3 s self-change window
        }

        Assert.Equal(attaches, h.Mode.ApplyCalls);
        Assert.Equal(attaches, h.Mode.RestoreCalls);
    }

    // ── Roadmap Phase-1 exit: kill -9 at 20 points → next start always valid ────

    /// <summary>Simulates kill -9: at the armed port call the "process" freezes — no unwind,
    /// no journal clear. The test releases the thread afterwards only so the test process
    /// itself can unwind cleanly.</summary>
    private sealed class KillSwitch
    {
        public int ArmedAt = -1; // 1-based port-call index at which to "die"; <= 0 disables
        private int _calls;
        public volatile bool Fired;
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnCall()
        {
            int n = Interlocked.Increment(ref _calls);
            if (n == ArmedAt)
            {
                Fired = true;
                Release.Task.Wait(); // the process is dead here
                throw new KillSignalException($"simulated kill -9 at port call {n}");
            }
        }
    }

    private sealed class KillSignalException : Exception
    {
        public KillSignalException(string message) : base(message) { }
    }

    /// <summary>One simulated Beam "process": a fresh manager + fakes sharing only the journal.</summary>
    private sealed class SimProcess : IDisposable
    {
        public SimProcess(DisplayTopology topology, FakeJournalStore journal, KillSwitch? kill)
        {
            Changes = new FakeChangeSource();
            Topology = new FakeTopologySource { Topology = topology };
            Mode = new FakeModeController();
            Mode.OnApply = () => Topology.Topology = Topologies.CloneFixed16x9();
            Dpi = new FakeDpiController { ReadResult = ScaleAt200 };
            NightLight = new FakeNightLightController();
            Session = new SessionManager();
            Manager = new PresentationManager(Changes, Topology, Mode, Dpi, NightLight, journal, Session);
            if (kill is not null)
            {
                Topology.BeforeCall = kill.OnCall;
                Mode.BeforeCall = kill.OnCall;
                Dpi.BeforeCall = kill.OnCall;
                NightLight.BeforeCall = kill.OnCall;
                journal.BeforeCall = kill.OnCall;
            }
        }

        public FakeChangeSource Changes { get; }
        public FakeTopologySource Topology { get; }
        public FakeModeController Mode { get; }
        public FakeDpiController Dpi { get; }
        public FakeNightLightController NightLight { get; }
        public SessionManager Session { get; }
        public PresentationManager Manager { get; }

        public void Dispose() => Manager.Dispose();
    }

    [Fact]
    public async Task Soak_KillAt20Points_NextStartAlwaysValid()
    {
        // Kill -9 at 20 distinct points across the apply → restore → apply sequences.
        // The only state that survives a kill is the journal, so the next start (a fresh
        // manager over the same journal) must always end valid: Idle, safe mode off, and
        // exactly one recovery restore iff the journal was left active.
        const int points = 20;
        for (int k = 1; k <= points; k++)
        {
            var journal = new FakeJournalStore();
            var kill = new KillSwitch { ArmedAt = k };

            using var p1 = new SimProcess(Topologies.MirroredWithMismatch(), journal, kill);
            Task run = Task.Run(() => p1.Manager.ApplyNowAsync());

            await WaitUntilAsync(() => kill.Fired || run.IsCompleted);
            if (!kill.Fired)
            {
                // Apply finished before the kill point: unplug to drive the restore sequence.
                // Raised on a pool thread: if the kill fires mid-handler, THAT thread blocks
                // in the kill switch — never the test thread (which would deadlock the run).
                _ = Task.Run(() => p1.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1)));
                // PendingTask is a stale reference here (it still holds phase 1's completed
                // task until OnDisplayChanged overwrites it), so wait for the restore to
                // actually finish: state becomes Idle only after MarkIdle runs, which is
                // after all restore port calls (Mode.Restore, Journal.Clear).
                await WaitUntilAsync(() => kill.Fired || p1.Session.State == SessionState.Idle);
            }

            if (!kill.Fired)
            {
                // Restore also finished: run a second apply to cover the later points.
                run = Task.Run(() => p1.Manager.ApplyNowAsync());
                await WaitUntilAsync(() => kill.Fired || run.IsCompleted);
            }

            Assert.True(kill.Fired, $"kill point {k} was never reached");
            bool journalWasActive = journal.Record is { IsActive: true };

            // "Next start": a fresh process sharing only the journal.
            using var p2 = new SimProcess(Topologies.MirroredWithMismatch(), journal, kill: null);
            await p2.Manager.TryRecoverAsync();

            Assert.Equal(SessionState.Idle, p2.Session.State); // never half-applied
            Assert.False(p2.Manager.IsSafeMode);
            if (journalWasActive)
            {
                Assert.Null(journal.Record);        // recovery cleared it
                Assert.Equal(1, p2.Mode.RestoreCalls); // exactly one recovery restore
            }
            else
            {
                Assert.Equal(0, p2.Mode.RestoreCalls); // nothing was persisted → nothing to do
            }

            // Let the "killed" process unwind after the assertions, so no thread leaks.
            kill.Release.SetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await p1.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // The killed process's last task may fault while unwinding — irrelevant here.
            }
        }
    }
}

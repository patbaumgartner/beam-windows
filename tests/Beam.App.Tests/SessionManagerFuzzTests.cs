using System.Collections.Concurrent;
using Beam.App.Sessions;

namespace Beam.App.Tests;

/// <summary>
/// Seeded fuzz over the session state machine: thousands of random operation sequences,
/// single-threaded and massively parallel. Invariants: the StateChanged chain stays
/// connected (each event's From is the previous event's To) and the settle sequence
/// Resume → RequestRestore → MarkIdle always lands in Idle from any reachable state.
/// </summary>
public class SessionManagerFuzzTests
{
    private static ulong Next(ref ulong seed)
    {
        seed ^= seed << 13;
        seed ^= seed >> 7;
        seed ^= seed << 17;
        return seed;
    }

    private static void ApplyRandomOp(SessionManager session, ref ulong seed)
    {
        switch ((int)(Next(ref seed) % 6))
        {
            case 0:
                session.TryBeginFix();
                break;
            case 1:
                session.MarkActive();
                break;
            case 2:
                session.RequestRestore();
                break;
            case 3:
                session.MarkIdle();
                break;
            case 4:
                session.Pause();
                break;
            default:
                session.Resume();
                break;
        }
    }

    private static void Settle(SessionManager session)
    {
        session.Resume();
        session.RequestRestore();
        session.MarkIdle();
    }

    private static void AssertChain(IEnumerable<SessionEvent> events)
    {
        SessionEvent? previous = null;
        foreach (SessionEvent e in events)
        {
            if (previous is not null)
            {
                Assert.Equal(previous.To, e.From);
            }
            else
            {
                Assert.Equal(SessionState.Idle, e.From);
            }

            previous = e;
        }
    }

    [Fact]
    public void Fuzz_20RunsOf2000RandomOps_ChainHoldsAndSettlesToIdle()
    {
        for (int run = 0; run < 20; run++)
        {
            ulong seed = 0x123456789ABCDEF0 + (ulong)run * 0x9E3779B97F4A7C15;
            using var session = new SessionManager();
            var events = new List<SessionEvent>();
            session.StateChanged += e => events.Add(e);

            for (int i = 0; i < 2000; i++)
            {
                ApplyRandomOp(session, ref seed);
            }

            Settle(session);
            Assert.Equal(SessionState.Idle, session.State);
            AssertChain(events);
        }
    }

    [Fact]
    public void Fuzz_Parallel_8ThreadsX500Ops_ChainHoldsAndSettlesToIdle()
    {
        // All transitions happen under the SessionManager lock, so the event stream is
        // totally ordered even with 8 threads hammering the machine at once.
        using var session = new SessionManager();
        var events = new ConcurrentQueue<SessionEvent>();
        session.StateChanged += e => events.Enqueue(e);

        Parallel.For(0, 8, t =>
        {
            ulong seed = 0xDEADBEEFCAFEF00D + (ulong)t * 0x9E3779B97F4A7C15;
            for (int i = 0; i < 500; i++)
            {
                ApplyRandomOp(session, ref seed);
            }
        });

        Settle(session);
        Assert.Equal(SessionState.Idle, session.State);
        AssertChain(events);
    }

    [Fact]
    public void Settle_FromEveryReachableState_LandsIdle()
    {
        var scenarios = new (string Name, Action<SessionManager> Setup)[]
        {
            ("Idle", s => { }),
            ("Applying", s => s.TryBeginFix()),
            ("Active", s => { s.TryBeginFix(); s.MarkActive(); }),
            ("Restoring", s => { s.TryBeginFix(); s.RequestRestore(); }),
            ("PausedFromIdle", s => s.Pause()),
            ("PausedFromActive", s => { s.TryBeginFix(); s.MarkActive(); s.Pause(); }),
        };

        foreach ((string name, Action<SessionManager> setup) in scenarios)
        {
            using var session = new SessionManager();
            setup(session);

            Settle(session);

            Assert.True(
                session.State == SessionState.Idle,
                $"settle from {name} left state={session.State}");
        }
    }
}

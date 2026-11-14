namespace Beam.Core.Startup;

/// <summary>
/// The per-user single-instance lock shared by the tray app and the CLI.
///
/// Both processes must agree on one name so that a <c>beam apply</c> from a shell
/// cannot race the tray's fix sequence (two processes calling SetDisplayConfig and
/// writing journal.json at the same time). The tray holds the mutex for its whole
/// lifetime; the CLI acquires it only for the duration of a mutating command.
///
/// <c>Local\</c> scopes the mutex to the current session, so a Beam running in another
/// user's session (or an RDP session) never blocks us.
/// </summary>
public static class SingleInstanceLock
{
    /// <summary>The mutex name. Shared by <c>Beam.Tray</c> and <c>Beam.Cli</c>.</summary>
    public const string MutexName = @"Local\Beam.SingleInstance";

    /// <summary>How long a mutating CLI command waits for the tray to finish before giving up.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Tries to acquire the single-instance mutex. Returns a lease that releases the
    /// mutex when disposed, or null when the lock could not be acquired within
    /// <paramref name="timeout"/> (another instance holds it, or the name is unusable).
    /// Never throws: a locking problem must degrade to "command refused", not a crash.
    /// </summary>
    /// <param name="timeout">Wait limit; defaults to <see cref="DefaultTimeout"/>.</param>
    /// <param name="mutexName">
    /// Overrides the mutex name (tests use a unique name per test so parallel test
    /// runs never contend on the production lock).
    /// </param>
    public static IDisposable? TryAcquire(TimeSpan? timeout = null, string? mutexName = null)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, mutexName ?? MutexName);
        }
        catch
        {
            return null; // unusable name — treat as "lock unavailable"
        }

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(timeout ?? DefaultTimeout);
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died without releasing: the wait succeeded anyway.
            acquired = true;
        }
        catch
        {
            mutex.Dispose();
            return null;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return null;
        }

        return new Lease(mutex);
    }

    /// <summary>Releases the mutex on dispose. Safe to dispose twice.</summary>
    private sealed class Lease : IDisposable
    {
        private Mutex? _mutex;

        public Lease(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // ApplicationException when the calling thread does not own the mutex —
                // nothing useful to do; the process is exiting anyway.
            }

            _mutex?.Dispose();
            _mutex = null;
        }
    }
}

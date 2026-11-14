using System.IO;

namespace Beam.App.Startup;

/// <summary>
/// Best-effort marker file at <c>%LOCALAPPDATA%\Beam\instance.lock</c> that records
/// which process currently holds the single-instance mutex
/// (<see cref="Beam.Core.Startup.SingleInstanceLock"/>).
///
/// The mutex itself is the real guard, but a mutex carries no owner information: any
/// same-session process can pre-create <c>Local\Beam.SingleInstance</c> and silently
/// block Beam. The lock file names the last known holder (PID + start time) so a
/// squat shows up in the log as "another instance (pid NNNN) is running" instead of
/// a bare "already running". The file is diagnostic only — it is never trusted for
/// mutual exclusion, and a stale file after a crash is harmless (and informative).
/// </summary>
public static class InstanceLockFile
{
    /// <summary>File name inside the Beam data directory.</summary>
    public const string FileName = "instance.lock";

    /// <summary>Default directory: <c>%LOCALAPPDATA%\Beam</c> (same as the journal and logs).</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Beam");

    /// <summary>
    /// Writes (or overwrites) the lock file with the current PID and UTC timestamp.
    /// Never throws: the file is diagnostics, not a guard.
    /// </summary>
    public static void Write(string? dir = null, int? pid = null)
    {
        try
        {
            dir ??= DefaultDirectory;
            Directory.CreateDirectory(dir);
            pid ??= Environment.ProcessId;
            File.WriteAllText(
                Path.Combine(dir, FileName),
                $"pid={pid}{Environment.NewLine}startedUtc={DateTime.UtcNow:O}{Environment.NewLine}");
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>
    /// Reads the PID recorded in the lock file, or null when there is no readable file.
    /// A stale file after a crash simply reports the last holder — callers decide
    /// whether that process is still alive.
    /// </summary>
    public static int? ReadPid(string? dir = null)
    {
        try
        {
            string path = Path.Combine(dir ?? DefaultDirectory, FileName);
            if (!File.Exists(path))
            {
                return null;
            }

            foreach (string rawLine in File.ReadAllText(path).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("pid=", StringComparison.Ordinal)
                    && int.TryParse(line.Substring(4), out int pid))
                {
                    return pid;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

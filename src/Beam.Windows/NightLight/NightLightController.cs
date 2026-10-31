using Beam.Core.NightLight;
using Microsoft.Win32;

namespace Beam.Windows.NightLight;

/// <summary>One Night Light state value found in the CloudStore.</summary>
public sealed record NightLightEntry(
    string KeyPath,
    string ValueName,
    byte[]? RawBytes,
    NightLightState? State,
    string? Error)
{
    public bool IsValid => State is not null;
}

/// <summary>A captured state value, sufficient to restore it later.</summary>
public sealed record NightLightSnapshot(
    string KeyPath,
    string ValueName,
    byte[] RawBytes,
    NightLightBlob Blob);

/// <summary>Result of a write operation, including per-key detail.</summary>
public sealed record NightLightWriteResult(
    bool Success,
    string Message,
    IReadOnlyList<string> Details);

/// <summary>
/// Reads and writes the real Windows Night Light toggle through the per-user CloudStore.
/// </summary>
/// <remarks>
/// <para>
/// There is no public Night Light API. The Settings app's toggle is persisted as a
/// binary CloudStore value; writing it applies immediately. The layout is undocumented,
/// so this class is deliberately strict: if a value cannot be parsed, it is never
/// written.
/// </para>
/// <para>
/// Windows reverts a write whose prologue epoch is not greater than the stored one, so
/// every write re-serializes with a fresh epoch instead of replaying raw bytes.
/// </para>
/// </remarks>
public sealed class NightLightController
{
    public const string ValueName = "Data";

    private const string CloudStoreCurrentPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current";

    public const string DefaultStateKeyPath =
        CloudStoreCurrentPath +
        @"\default$windows.data.bluelightreduction.bluelightreductionstate" +
        @"\windows.data.bluelightreduction.bluelightreductionstate";

    private const string PerDeviceSuffix =
        "$windows.data.bluelightreduction.bluelightreductionstateperdevice";

    /// <summary>
    /// Returns the default state value plus every per-device state value (26H2+).
    /// </summary>
    public IReadOnlyList<string> EnumerateStateKeyPaths()
    {
        var paths = new List<string> { DefaultStateKeyPath };

        using RegistryKey? current = Registry.CurrentUser.OpenSubKey(CloudStoreCurrentPath);
        if (current is null)
        {
            return paths;
        }

        foreach (string name in current.GetSubKeyNames())
        {
            if (!name.EndsWith(PerDeviceSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            paths.Add($@"{CloudStoreCurrentPath}\{name}\windows.data.bluelightreduction.bluelightreductionstateperdevice");
        }

        return paths;
    }

    /// <summary>Reads and parses every state value. Never throws for a malformed value.</summary>
    public IReadOnlyList<NightLightEntry> ReadAll()
    {
        var entries = new List<NightLightEntry>();

        foreach (string keyPath in EnumerateStateKeyPaths())
        {
            entries.Add(Read(keyPath));
        }

        return entries;
    }

    /// <summary>Reads and parses one state value.</summary>
    public NightLightEntry Read(string keyPath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
            if (key is null)
            {
                return new NightLightEntry(keyPath, ValueName, null, null, "Registry key not found.");
            }

            if (key.GetValue(ValueName) is not byte[] raw || raw.Length == 0)
            {
                return new NightLightEntry(keyPath, ValueName, null, null, "Value 'Data' is missing or empty.");
            }

            NightLightBlob blob = NightLightBlobCodec.Parse(raw);
            return new NightLightEntry(keyPath, ValueName, raw, blob.State, null);
        }
        catch (NightLightFormatException ex)
        {
            return new NightLightEntry(keyPath, ValueName, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            // A value we cannot read is a value we must not write. Never let an
            // unexpected exception escape into the app.
            return new NightLightEntry(keyPath, ValueName, null, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Captures every parsable state value.</summary>
    public IReadOnlyList<NightLightSnapshot> SnapshotAll()
    {
        var snapshots = new List<NightLightSnapshot>();

        foreach (NightLightEntry entry in ReadAll())
        {
            if (entry.RawBytes is null || entry.State is null)
            {
                continue;
            }

            NightLightBlob blob = NightLightBlobCodec.Parse(entry.RawBytes);
            snapshots.Add(new NightLightSnapshot(entry.KeyPath, entry.ValueName, entry.RawBytes, blob));
        }

        return snapshots;
    }

    /// <summary>
    /// Turns Night Light on or off for every state value. Verifies by re-reading.
    /// </summary>
    public NightLightWriteResult SetActive(bool active)
    {
        var details = new List<string>();
        bool allSucceeded = true;
        DateTimeOffset now = DateTimeOffset.Now;

        foreach (NightLightEntry entry in ReadAll())
        {
            if (entry.RawBytes is null || entry.State is null)
            {
                allSucceeded = false;
                details.Add($"{Short(entry.KeyPath)}: skipped ({entry.Error})");
                continue;
            }

            if (entry.State.Active == active && entry.State.TransitionCause == NightLightTransitionCause.Manual)
            {
                details.Add($"{Short(entry.KeyPath)}: already {(active ? "on" : "off")} (no write)");
                continue;
            }

            try
            {
                NightLightBlob blob = NightLightBlobCodec.Parse(entry.RawBytes);
                var desired = new NightLightState(
                    active,
                    NightLightTransitionCause.Manual,
                    entry.State.PrologueEpochSeconds,
                    entry.State.ModifiedFileTime);

                byte[] updated = NightLightBlobCodec.Serialize(blob, desired, now);

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(entry.KeyPath, writable: true)
                       ?? throw new InvalidOperationException("Key not writable."))
                {
                    key.SetValue(ValueName, updated, RegistryValueKind.Binary);
                }

                NightLightEntry verify = Read(entry.KeyPath);
                if (verify.State is not null && verify.State.Active == active)
                {
                    details.Add($"{Short(entry.KeyPath)}: {(active ? "on" : "off")} verified ({updated.Length} bytes)");
                }
                else
                {
                    allSucceeded = false;
                    details.Add($"{Short(entry.KeyPath)}: verification failed (active={verify.State?.Active})");
                }
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                details.Add($"{Short(entry.KeyPath)}: write failed ({ex.GetType().Name}: {ex.Message})");
            }
        }

        string message = allSucceeded
            ? $"Night Light {(active ? "on" : "off")} verified."
            : $"Night Light {(active ? "on" : "off")} partially failed.";

        return new NightLightWriteResult(allSucceeded, message, details);
    }

    /// <summary>
    /// Restores captured states. The epoch is advanced against the current value so
    /// Windows does not revert the write; the semantic state and unknown suffix bytes
    /// are restored exactly.
    /// </summary>
    public NightLightWriteResult Restore(IReadOnlyList<NightLightSnapshot> snapshots)
    {
        var details = new List<string>();
        bool allSucceeded = true;
        DateTimeOffset now = DateTimeOffset.Now;

        foreach (NightLightSnapshot snapshot in snapshots)
        {
            NightLightEntry current = Read(snapshot.KeyPath);
            if (current.State is null)
            {
                allSucceeded = false;
                details.Add($"{Short(snapshot.KeyPath)}: skipped ({current.Error})");
                continue;
            }

            try
            {
                // Keep the current epoch as the floor so the write is accepted, but
                // restore the snapshot's semantic state and unknown suffix bytes.
                var floored = new NightLightBlob(
                    snapshot.Blob.State with { PrologueEpochSeconds = current.State.PrologueEpochSeconds },
                    snapshot.Blob.Suffix);

                byte[] restored = NightLightBlobCodec.Serialize(floored, snapshot.Blob.State, now);

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(snapshot.KeyPath, writable: true)
                       ?? throw new InvalidOperationException("Key not writable."))
                {
                    key.SetValue(snapshot.ValueName, restored, RegistryValueKind.Binary);
                }

                NightLightEntry verify = Read(snapshot.KeyPath);
                if (verify.State is not null
                    && verify.State.Active == snapshot.Blob.State.Active
                    && verify.State.TransitionCause == snapshot.Blob.State.TransitionCause)
                {
                    details.Add($"{Short(snapshot.KeyPath)}: restored (active={verify.State.Active}, {verify.State.TransitionCause})");
                }
                else
                {
                    allSucceeded = false;
                    details.Add($"{Short(snapshot.KeyPath)}: restore verification failed");
                }
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                details.Add($"{Short(snapshot.KeyPath)}: restore failed ({ex.GetType().Name}: {ex.Message})");
            }
        }

        return new NightLightWriteResult(
            allSucceeded,
            allSucceeded ? "Night Light restored." : "Night Light restore partially failed.",
            details);
    }

    private static string Short(string keyPath)
    {
        if (keyPath.Contains("perdevice", StringComparison.OrdinalIgnoreCase))
        {
            return "per-device";
        }

        return keyPath.Contains("bluelightreductionstate", StringComparison.OrdinalIgnoreCase)
            ? "default"
            : keyPath;
    }
}

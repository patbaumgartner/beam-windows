using System.IO;
using System.Text.Json;
using Beam.Windows.NightLight;
using Beam.Core.NightLight;

namespace Beam.Tray;

/// <summary>
/// Crash-safe journal. Persists the fact that a presentation fix is active (and the
/// Night Light state captured before it) so that if Beam is killed mid-fix, the next
/// launch can detect the leftover state and restore the original configuration.
///
/// Location: <c>%LOCALAPPDATA%\Beam\journal.json</c>. Writes are atomic (write to a
/// temp file and rename) so a crash never produces a half-written journal.
/// </summary>
/// <remarks>
/// Display-mode restoration of per-monitor DEVMODEs cannot be fully replayed from JSON
/// (the shell owns those states), so the journal records intent plus the complete Night
/// Light snapshot. On recovery the app returns to extended topology and re-enables the
/// captured Night Light state — the two things Beam changed that are worth undoing.
/// </remarks>
internal sealed class JournalStore
{
    private readonly string _path;
    private readonly string _tmpPath;

    public JournalStore(string? dir = null)
    {
        dir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Beam");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "journal.json");
        _tmpPath = _path + ".tmp";
    }

    internal sealed record NightLightSnapshotEntry(string KeyPath, string ValueName, string RawBytesBase64);

    internal sealed record Journal(
        bool IsActive,
        string Reason,
        string AppliedAt,
        IReadOnlyList<NightLightSnapshotEntry>? NightLight);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>Persists that a fix sequence has started, with the captured Night Light state.</summary>
    public void BeginApply(string reason, IReadOnlyList<NightLightSnapshot>? nightLight)
    {
        var journal = new Journal(
            IsActive: true,
            Reason: reason,
            AppliedAt: DateTimeOffset.Now.ToString("o"),
            NightLight: nightLight?.Select(n => new NightLightSnapshotEntry(
                n.KeyPath, n.ValueName, Convert.ToBase64String(n.RawBytes))).ToList());
        Write(journal);
    }

    /// <summary>Marks the journal idle (fix fully applied and safe, or restored).</summary>
    public void MarkIdle(string reason)
    {
        Write(new Journal(false, reason + " (idle)", DateTimeOffset.Now.ToString("o"), null));
    }

    /// <summary>Reads the journal, or null when none exists.</summary>
    public Journal? Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }
            return JsonSerializer.Deserialize<Journal>(File.ReadAllText(_path), Options);
        }
        catch
        {
            // A corrupt journal must not prevent the app from starting.
            return null;
        }
    }

    /// <summary>Removes the journal file entirely (clean shutdown / restore complete).</summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            if (File.Exists(_tmpPath))
            {
                File.Delete(_tmpPath);
            }
        }
        catch
        {
            // Best-effort; a stale journal just triggers a harmless recover attempt.
        }
    }

    private void Write(Journal journal)
    {
        try
        {
            string json = JsonSerializer.Serialize(journal, Options);
            File.WriteAllText(_tmpPath, json);
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            File.Move(_tmpPath, _path);
        }
        catch
        {
            // A journal write failure must never take the app down.
        }
    }

    /// <summary>Rehydrates Night Light snapshots from a journal's base64 entries.</summary>
    public static IReadOnlyList<NightLightSnapshot>? RehydrateNightLight(Journal? journal)
    {
        if (journal?.NightLight is not { Count: > 0 })
        {
            return null;
        }

        var list = new List<NightLightSnapshot>(journal.NightLight.Count);
        foreach (NightLightSnapshotEntry e in journal.NightLight)
        {
            try
            {
                byte[] raw = Convert.FromBase64String(e.RawBytesBase64);
                var blob = NightLightBlobCodec.Parse(raw);
                list.Add(new NightLightSnapshot(e.KeyPath, e.ValueName, raw, blob));
            }
            catch
            {
                // Skip unparseable entries; still try to restore the ones we can.
            }
        }
        return list.Count == 0 ? null : list;
    }
}

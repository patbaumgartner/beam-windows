using Beam.Core.NightLight;

namespace Beam.App.Journal;

/// <summary>One persisted Night Light value, stored as base64 so JSON stays text-only.</summary>
public sealed record JournalEntry(string KeyPath, string ValueName, string RawBytesBase64);

/// <summary>
/// One persisted original DPI scale: the percent Beam changed and must put back on
/// restore/recovery (roadmap 1.9). Keyed by adapter LUID + source id.
/// </summary>
public sealed record JournalDpiEntry(string AdapterId, uint SourceId, int Percent);

/// <summary>
/// B3: the presentation extras Beam switched on during a fix (keep-awake hold, DND/toasts
/// off, audio on an HDMI endpoint), with the captured previous state so restore — including
/// crash recovery — can put each one back exactly. All values are optional: an extra that
/// was not engaged (policy off, or the platform call failed) is simply absent.
/// </summary>
public sealed record JournalExtras(
    bool? ToastsWereEnabled = null,
    string? PreviousAudioEndpoint = null);

/// <summary>
/// The on-disk crash journal. <see cref="IsActive"/> means a fix sequence started and has
/// not been restored yet, so the next launch must undo it.
///
/// <see cref="Version"/> is the journal schema version. It defaults to
/// <see cref="CurrentVersion"/> so journals written by older 0.1.x builds (which have no
/// version field) still deserialize; readers ignore records claiming a newer version.
/// </summary>
public sealed record JournalRecord(
    bool IsActive,
    string Reason,
    string AppliedAt,
    IReadOnlyList<JournalEntry>? NightLight,
    IReadOnlyList<JournalDpiEntry>? Dpi = null,
    int Version = JournalRecord.CurrentVersion,
    JournalExtras? Extras = null)
{
    /// <summary>Schema version written by this build. Bump on any breaking change.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists the intent to change display state, so a crash mid-sequence can be recovered
/// on the next launch. The application layer depends on this interface; tests use a fake.
/// </summary>
public interface IJournalStore
{
    /// <summary>
    /// Persists that a fix sequence has started, with the captured Night Light state,
    /// the original DPI scale per source (captured before any mutation), and the B3
    /// extras state (toasts, previous audio endpoint) captured before any mutation.
    /// </summary>
    void BeginApply(
        string reason,
        IReadOnlyList<NightLightSnapshot>? nightLight,
        IReadOnlyList<JournalDpiEntry>? dpi = null,
        JournalExtras? extras = null);

    /// <summary>Reads the journal, or null when none exists or it cannot be parsed.</summary>
    JournalRecord? Read();

    /// <summary>Removes the journal file entirely (restore complete / clean shutdown).</summary>
    void Clear();
}

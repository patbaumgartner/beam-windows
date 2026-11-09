using Beam.Core.Display;
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
/// The on-disk crash journal. <see cref="IsActive"/> means a fix sequence started and has
/// not been restored yet, so the next launch must undo it.
/// </summary>
public sealed record JournalRecord(
    bool IsActive,
    string Reason,
    string AppliedAt,
    IReadOnlyList<JournalEntry>? NightLight,
    IReadOnlyList<JournalDpiEntry>? Dpi = null);

/// <summary>
/// Persists the intent to change display state, so a crash mid-sequence can be recovered
/// on the next launch. The application layer depends on this interface; tests use a fake.
/// </summary>
public interface IJournalStore
{
    /// <summary>
    /// Persists that a fix sequence has started, with the captured Night Light state and
    /// the original DPI scale per source (captured before any mutation).
    /// </summary>
    void BeginApply(
        string reason,
        IReadOnlyList<NightLightSnapshot>? nightLight,
        IReadOnlyList<JournalDpiEntry>? dpi = null);

    /// <summary>Reads the journal, or null when none exists or it cannot be parsed.</summary>
    JournalRecord? Read();

    /// <summary>Removes the journal file entirely (restore complete / clean shutdown).</summary>
    void Clear();
}

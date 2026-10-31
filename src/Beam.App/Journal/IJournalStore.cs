using Beam.Core.NightLight;

namespace Beam.App.Journal;

/// <summary>One persisted Night Light value, stored as base64 so JSON stays text-only.</summary>
public sealed record JournalEntry(string KeyPath, string ValueName, string RawBytesBase64);

/// <summary>
/// The on-disk crash journal. <see cref="IsActive"/> means a fix sequence started and has
/// not been restored yet, so the next launch must undo it.
/// </summary>
public sealed record JournalRecord(
    bool IsActive,
    string Reason,
    string AppliedAt,
    IReadOnlyList<JournalEntry>? NightLight);

/// <summary>
/// Persists the intent to change display state, so a crash mid-sequence can be recovered
/// on the next launch. The application layer depends on this interface; tests use a fake.
/// </summary>
public interface IJournalStore
{
    /// <summary>Persists that a fix sequence has started, with the captured Night Light state.</summary>
    void BeginApply(string reason, IReadOnlyList<NightLightSnapshot>? nightLight);

    /// <summary>Reads the journal, or null when none exists or it cannot be parsed.</summary>
    JournalRecord? Read();

    /// <summary>Removes the journal file entirely (restore complete / clean shutdown).</summary>
    void Clear();
}

using Beam.App.Journal;
using Beam.Core.NightLight;

namespace Beam.App.Tests;

/// <summary>Round-trip tests for the crash-safe journal.</summary>
public class JournalStoreTests
{
    /// <summary>Real default Night Light value, 38 bytes, off via schedule.</summary>
    private static readonly byte[] RealDefaultOff =
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06,
        0x86, 0xE6, 0x8B, 0xD7, 0x06,
        0x2A, 0x2B, 0x0E, 0x10, 0x43, 0x42, 0x01,
        0x00, 0xC6, 0x14,
        0xAA, 0x8E, 0xD0, 0xE6, 0xD8, 0xEE, 0xD9, 0xEE, 0x01,
        0x00, 0x00, 0x00, 0x00,
    ];

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "beam-journal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static NightLightSnapshot Snapshot() =>
        new(@"KEY\path", "Data", RealDefaultOff, NightLightBlobCodec.Parse(RealDefaultOff));

    [Fact]
    public void BeginApply_WritesActiveJournal_ReadReturnsIt()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            store.BeginApply("test-fix", new[] { Snapshot() });

            JournalRecord? j = store.Read();

            Assert.NotNull(j);
            Assert.True(j!.IsActive);
            Assert.Equal("test-fix", j.Reason);
            Assert.NotNull(j.AppliedAt);
            Assert.NotNull(j.NightLight);
            Assert.Single(j.NightLight!);
            Assert.Equal(@"KEY\path", j.NightLight![0].KeyPath);
            Assert.Equal("Data", j.NightLight![0].ValueName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BeginApply_WritesHumanReadableIndentedJson()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            store.BeginApply("readable", null);

            string json = File.ReadAllText(Path.Combine(dir, "journal.json"));
            Assert.Contains(Environment.NewLine, json);
            Assert.Contains("  \"IsActive\": true", json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BeginApply_StaysActiveUntilCleared_SoACrashCanRecover()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            store.BeginApply("test-fix", null);

            // The journal deliberately stays active while the fix owns the display:
            // a crash or a normal exit mid-presentation must trigger recovery next launch.
            JournalRecord? j = store.Read();

            Assert.NotNull(j);
            Assert.True(j!.IsActive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Clear_RemovesJournal_ReadReturnsNull()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            store.BeginApply("test-fix", null);
            store.Clear();

            Assert.Null(store.Read());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RehydrateNightLight_RoundTripsRawBytes()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            store.BeginApply("test-fix", new[] { Snapshot() });

            var restored = JournalStore.RehydrateNightLight(store.Read());

            Assert.NotNull(restored);
            Assert.Single(restored!);
            Assert.Equal(@"KEY\path", restored![0].KeyPath);
            Assert.Equal("Data", restored[0].ValueName);
            Assert.Equal(RealDefaultOff, restored[0].RawBytes);
            Assert.False(restored[0].Blob.State.Active);
            Assert.Equal(NightLightTransitionCause.Schedule, restored[0].Blob.State.TransitionCause);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            Assert.Null(store.Read());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_CorruptJson_ReturnsNull_DoesNotThrow()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "journal.json"), "{ not valid json !!!");
            var store = new JournalStore(dir);

            // Must not throw — a corrupt journal must not prevent startup.
            Assert.Null(store.Read());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Phase A (A2): atomic writes + schema hardening ──────────────────────────

    [Fact]
    public void BeginApply_WritesCurrentSchemaVersion()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            store.BeginApply("test-fix", null);

            Assert.Equal(JournalRecord.CurrentVersion, store.Read()!.Version);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_Twice_ReplacesAtomically_AndLeavesNoTmpFile()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            store.BeginApply("first", null);
            store.BeginApply("second", null);

            Assert.Equal("second", store.Read()!.Reason);
            Assert.False(
                File.Exists(Path.Combine(dir, "journal.json.tmp")),
                "the temp file must not linger after a successful atomic write");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_LegacyJournalWithoutVersion_DefaultsToCurrentVersion()
    {
        string dir = TempDir();
        try
        {
            // A 0.1.x journal has no Version field; it must still load.
            File.WriteAllText(Path.Combine(dir, "journal.json"),
                """{"IsActive":true,"Reason":"old","AppliedAt":"2026-01-01T00:00:00+00:00","NightLight":null,"Dpi":null}""");
            var store = new JournalStore(dir);

            JournalRecord? j = store.Read();

            Assert.NotNull(j);
            Assert.Equal(JournalRecord.CurrentVersion, j!.Version);
            Assert.Equal("old", j.Reason);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_JournalFromNewerVersion_ReturnsNull()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "journal.json"),
                """{"IsActive":true,"Reason":"future","AppliedAt":"2026-01-01T00:00:00+00:00","NightLight":null,"Dpi":null,"Version":99}""");
            var store = new JournalStore(dir);

            Assert.Null(store.Read());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BeginApply_WriteFailure_ThrowsAndPreservesPreviousJournal()
    {
        string dir = TempDir();
        try
        {
            var store = new JournalStore(dir);
            store.BeginApply("known-good", null);
            string before = File.ReadAllText(Path.Combine(dir, "journal.json"));
            Directory.CreateDirectory(Path.Combine(dir, "journal.json.tmp"));

            Assert.Throws<IOException>(() => store.BeginApply("must-not-replace", null));

            Assert.Equal(before, File.ReadAllText(Path.Combine(dir, "journal.json")));
            Assert.Equal("known-good", store.Read()!.Reason);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_OversizedJournal_ReturnsNull()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "journal.json"),
                new string('x', (int)JournalStore.MaxReadBytes + 1));
            var store = new JournalStore(dir);

            Assert.Null(store.Read());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

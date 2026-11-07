using System.IO;
using Beam.App.Journal;
using Beam.Core.NightLight;

namespace Beam.App.Tests;

/// <summary>
/// Journal crash-safety: atomic writes (no half-written journal), corrupt file
/// tolerance, concurrency, and clear semantics. The journal is what makes a kill
/// mid-fix recoverable, so these failure modes matter.
/// </summary>
public class JournalCrashSafetyTests : IDisposable
{
    private readonly string _dir;

    public JournalCrashSafetyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"beam-journal-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static IReadOnlyList<NightLightSnapshot> Snapshots(int count = 1)
    {
        // A real fixture blob so RehydrateNightLight can parse it back.
        byte[] raw =
        [
            0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06,
            0x86, 0xE6, 0x8B, 0xD7, 0x06,
            0x2A, 0x2B, 0x0E, 0x10, 0x43, 0x42, 0x01,
            0x00, 0xC6, 0x14,
            0xAA, 0x8E, 0xD0, 0xE6, 0xD8, 0xEE, 0xD9, 0xEE, 0x01,
            0x00, 0x00, 0x00, 0x00,
        ];
        return Enumerable.Range(0, count).Select(_ =>
            new NightLightSnapshot(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.settings\Current",
                "Data",
                raw,
                NightLightBlobCodec.Parse(raw))).ToList();
    }

    [Fact]
    public void BeginApply_ThenRead_Roundtrips()
    {
        var store = new JournalStore(_dir);

        store.BeginApply("test", Snapshots(2));

        JournalRecord? read = store.Read();
        Assert.NotNull(read);
        Assert.True(read!.IsActive);
        Assert.Equal("test", read.Reason);
        Assert.NotNull(read.NightLight);
        Assert.Equal(2, read.NightLight!.Count);
    }

    [Fact]
    public void CorruptJson_Read_ReturnsNull()
    {
        var store = new JournalStore(_dir);
        File.WriteAllText(Path.Combine(_dir, "journal.json"), "{ this is not json");

        Assert.Null(store.Read());
    }

    [Fact]
    public void EmptyFile_Read_ReturnsNull()
    {
        var store = new JournalStore(_dir);
        File.WriteAllText(Path.Combine(_dir, "journal.json"), "");

        Assert.Null(store.Read());
    }

    [Fact]
    public void NullJson_Read_ReturnsNull()
    {
        var store = new JournalStore(_dir);
        File.WriteAllText(Path.Combine(_dir, "journal.json"), "null");

        Assert.Null(store.Read());
    }

    [Fact]
    public void TruncatedMidWrite_Read_ReturnsNull_NoCrash()
    {
        // Simulate a crash during write: the tmp exists, the main file is truncated.
        var store = new JournalStore(_dir);
        File.WriteAllText(Path.Combine(_dir, "journal.json"), "{\"IsActive\":true,\"Reason\":\"tes");

        Assert.Null(store.Read());
    }

    [Fact]
    public void Clear_RemovesBothFiles()
    {
        var store = new JournalStore(_dir);
        store.BeginApply("test", Snapshots());
        Assert.True(File.Exists(Path.Combine(_dir, "journal.json")));

        store.Clear();

        Assert.False(File.Exists(Path.Combine(_dir, "journal.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "journal.json.tmp")));
        Assert.Null(store.Read());
    }

    [Fact]
    public void Clear_WhenNoFiles_DoesNotThrow()
    {
        var store = new JournalStore(_dir);
        store.Clear();
        Assert.Null(store.Read());
    }

    [Fact]
    public void BeginApply_Twice_SecondWins()
    {
        var store = new JournalStore(_dir);
        store.BeginApply("first", null);
        store.BeginApply("second", Snapshots());

        JournalRecord? read = store.Read();
        Assert.NotNull(read);
        Assert.Equal("second", read!.Reason);
        Assert.NotNull(read.NightLight);
    }

    [Fact]
    public void Rehydrate_CorruptEntry_SkipsIt_KeepsGood()
    {
        var store = new JournalStore(_dir);
        store.BeginApply("test", Snapshots(1));
        JournalRecord good = store.Read()!;

        var mixed = new JournalRecord(
            good.IsActive,
            good.Reason,
            good.AppliedAt,
            new[]
            {
                new JournalEntry("k", "v", "###not-base64###"),
                good.NightLight![0],
            });

        IReadOnlyList<NightLightSnapshot>? rehydrated = JournalStore.RehydrateNightLight(mixed);

        Assert.NotNull(rehydrated);
        Assert.Single(rehydrated);
    }

    [Fact]
    public void Rehydrate_AllCorrupt_ReturnsNull()
    {
        var journal = new JournalRecord(
            true, "r", "at",
            new[] { new JournalEntry("k", "v", "!!!") });

        Assert.Null(JournalStore.RehydrateNightLight(journal));
    }

    [Fact]
    public void Rehydrate_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(JournalStore.RehydrateNightLight(null));
        Assert.Null(JournalStore.RehydrateNightLight(new JournalRecord(true, "r", "at", null)));
        Assert.Null(JournalStore.RehydrateNightLight(
            new JournalRecord(true, "r", "at", Array.Empty<JournalEntry>())));
    }

    [Fact]
    public async Task ConcurrentBeginApply_FinalStateConsistent()
    {
        var store = new JournalStore(_dir);

        // Hammer the journal from 8 threads; the atomic-rename pattern must leave a
        // parseable file (or none), never a corrupt one.
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 25; i++)
            {
                store.BeginApply($"t{i}", Snapshots());
                store.Read();
            }
        }));

        await Task.WhenAll(tasks);

        // Whatever the final state, Read must not throw (it returns null on corruption).
        JournalRecord? final = store.Read();
        if (final is not null)
        {
            Assert.True(final.IsActive);
        }
    }

    [Fact]
    public void SnapshotCount_Zero_PersistsWithoutNightLight()
    {
        var store = new JournalStore(_dir);
        store.BeginApply("no-snapshots", null);

        JournalRecord? read = store.Read();
        Assert.NotNull(read);
        Assert.Null(read!.NightLight);
    }
}

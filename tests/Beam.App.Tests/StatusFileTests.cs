using Beam.App.Sessions;
using Beam.App.Status;
using Beam.Core.Display;
using Beam.Core.Status;

namespace Beam.App.Tests;

/// <summary>B6: the status.json file store — atomic writes, capped/corrupt-safe reads.</summary>
public class StatusFileTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "beam-status-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static StatusFileRecord Sample() => new(
        "Active", "fix sequence complete", "2026-11-14T10:00:00+00:00", 4321,
        "1920x1080@60", 100, true);

    [Fact]
    public void Write_ThenTryRead_RoundTrips()
    {
        string dir = TempDir();
        try
        {
            var file = new StatusFile(dir);
            file.Write(Sample());

            StatusFileRecord? read = StatusFile.TryRead(dir);

            Assert.NotNull(read);
            Assert.Equal("Active", read!.State);
            Assert.Equal("fix sequence complete", read.Reason);
            Assert.Equal(4321, read.Pid);
            Assert.Equal("1920x1080@60", read.AppliedSourceMode);
            Assert.Equal(100, read.AppliedDpiPercent);
            Assert.Equal(true, read.NightLightDisabled);
            // Atomic write: the temp file is gone, only status.json remains.
            Assert.False(File.Exists(Path.Combine(dir, "status.json.tmp")));
            Assert.True(File.Exists(Path.Combine(dir, "status.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_ReplacesPrevious_Atomically()
    {
        string dir = TempDir();
        try
        {
            var file = new StatusFile(dir);
            file.Write(Sample());
            file.Write(Sample() with
            {
                State = "Idle",
                Reason = (string?)null,
                AppliedSourceMode = null,
                AppliedDpiPercent = null,
                NightLightDisabled = null,
            });

            StatusFileRecord? read = StatusFile.TryRead(dir);

            Assert.NotNull(read);
            Assert.Equal("Idle", read!.State);
            Assert.Null(read.Reason);
            Assert.Null(read.AppliedSourceMode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Clear_RemovesFile_TryReadReturnsNull()
    {
        string dir = TempDir();
        try
        {
            var file = new StatusFile(dir);
            file.Write(Sample());
            file.Clear();

            Assert.Null(StatusFile.TryRead(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryRead_NoFile_ReturnsNull()
    {
        string dir = TempDir();
        try
        {
            Assert.Null(StatusFile.TryRead(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryRead_CorruptFile_ReturnsNull()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "status.json"), "{ not json !!!");
            Assert.Null(StatusFile.TryRead(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryRead_OversizeFile_ReturnsNull()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "status.json"), new string('x', (int)StatusFile.MaxReadBytes + 1));
            Assert.Null(StatusFile.TryRead(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>
/// B6: PresentationManager publishes the live status on every session transition and
/// clears it on dispose, so `beam status` reflects the running tray.
/// </summary>
public class PresentationStatusTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    [Fact]
    public async Task FixSequence_PublishesEveryTransition_EndingActiveWithAppliedInfo()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        await h.Manager.ApplyNowAsync();

        Assert.NotEmpty(h.Status.Writes);
        Assert.Equal("Idle", h.Status.Writes[0].State); // initial startup write
        Assert.Equal(
            new[] { "Idle", "Detecting", "Applying", "Active" },
            h.Status.Writes.Select(w => w.State).ToArray());

        StatusFileRecord last = h.Status.Writes[^1];
        Assert.Equal("Active", last.State);
        Assert.Equal(Environment.ProcessId, last.Pid);
        Assert.False(string.IsNullOrEmpty(last.UpdatedAtUtc));
        Assert.Equal("1920x1080@60", last.AppliedSourceMode); // fixed 16:9 shared source
        Assert.Equal(100, last.AppliedDpiPercent);             // 1920px at 200% → 100%
        Assert.Equal(true, last.NightLightDisabled);
    }

    [Fact]
    public async Task Restore_PublishesIdleWithNoAppliedInfo_AndDisposeClears()
    {
        var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        await h.Manager.RestoreNowAsync();

        Assert.Equal("Idle", h.Status.Writes[^1].State);
        Assert.Null(h.Status.Writes[^1].AppliedSourceMode);
        Assert.Null(h.Status.Writes[^1].AppliedDpiPercent);
        Assert.Null(h.Status.Writes[^1].NightLightDisabled);

        h.Manager.Dispose();
        Assert.Equal(1, h.Status.ClearCalls);
    }

    [Fact]
    public async Task NoStatusWriter_NoWrites_NoThrow()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, wireStatus: false);

        await h.Manager.ApplyNowAsync();
        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Empty(h.Status.Writes);
    }
}

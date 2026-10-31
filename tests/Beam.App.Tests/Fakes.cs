using Beam.App.Journal;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Ports;

namespace Beam.App.Tests;

/// <summary>Builders for the display topologies used across the application tests.</summary>
internal static class Topologies
{
    public static DisplayPath Path(
        uint sourceId,
        uint targetId,
        string targetName,
        bool isInternal,
        bool active,
        uint desktopW,
        uint desktopH,
        uint signalW,
        uint signalH) =>
        new(
            $@"\\.\DISPLAY{sourceId + 1}",
            sourceId,
            "0001594D:00000000",
            targetId,
            new DisplayTarget(targetName, "SHP", 0x1551, "HDMI", $@"monitor\{targetName}", isInternal, true),
            active,
            new DisplayMode(desktopW, desktopH, 60),
            new DisplayMode(signalW, signalH, 60),
            0, 0, "0", null, null, null, null);

    /// <summary>Mirrored external display whose 16:10 desktop leaves bars on the 16:9 signal.</summary>
    public static DisplayTopology MirroredWithMismatch() => new(
        new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 3840, 2400, 3840, 2400),
            Path(1, 2, "DELL", isInternal: false, active: true, 3840, 2400, 3840, 2160),
        },
        DisplayTopologyId.Clone);

    /// <summary>Mirrored 1080p desktop: the DPI planner will want 100%.</summary>
    public static DisplayTopology MirroredNeedScale() => new(
        new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 1920, 1080, 1920, 1080),
            Path(1, 2, "DELL", isInternal: false, active: true, 1920, 1080, 1920, 1200),
        },
        DisplayTopologyId.Clone);

    public static DisplayTopology Extended() => new(
        new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 3840, 2400, 3840, 2400),
            Path(1, 2, "DELL", isInternal: false, active: true, 3840, 2400, 3840, 2160),
        },
        DisplayTopologyId.Extend);

    public static ConnectionSnapshot Snapshot(int displayCount) =>
        new(
            Enumerable.Range(0, displayCount)
                .Select(i => new DisplaySnapshot(new DisplayId($"ADAPTER{i}", (uint)i), 1920, 1080, 100, $"D{i}"))
                .ToArray(),
            DateTimeOffset.UtcNow);
}

internal sealed class FakeTopologySource : ITopologySource
{
    public DisplayTopology Topology { get; set; } = DisplayTopology.Empty;
    public int QueryCount { get; private set; }

    public DisplayTopology Query()
    {
        QueryCount++;
        return Topology;
    }
}

internal sealed class FakeChangeSource : IDisplayChangeSource
{
    public event Action<ConnectionSnapshot?, ConnectionSnapshot>? Changed;

    public bool Started { get; private set; }
    public bool Stopped { get; private set; }

    public void Start() => Started = true;

    public void Stop() => Stopped = true;

    public void Raise(ConnectionSnapshot? previous, ConnectionSnapshot current) =>
        Changed?.Invoke(previous, current);
}

internal sealed class FakeModeController : IDisplayModeController
{
    public string? ApplyResult { get; set; }
    public string? RestoreResult { get; set; }
    public int ApplyCalls { get; private set; }
    public int RestoreCalls { get; private set; }

    public string? ApplyCloneWith16x9()
    {
        ApplyCalls++;
        return ApplyResult;
    }

    public string? Restore()
    {
        RestoreCalls++;
        return RestoreResult;
    }
}

internal sealed class FakeDpiController : IDpiController
{
    public DpiScale? ReadResult { get; set; }
    public bool SetResult { get; set; } = true;
    public List<int> RequestedPercents { get; } = new();

    public DpiScale? Read(DisplayPath path) => ReadResult;

    public bool SetPercentageAndVerify(DisplayPath path, int percent, DpiScaleValue? current = null)
    {
        RequestedPercents.Add(percent);
        return SetResult;
    }
}

internal sealed class FakeNightLightController : INightLightController
{
    public IReadOnlyList<NightLightSnapshot> Snapshots { get; set; } = Array.Empty<NightLightSnapshot>();
    public bool WriteResult { get; set; } = true;
    public int SnapshotCalls { get; private set; }
    public int SetActiveCalls { get; private set; }
    public int RestoreCalls { get; private set; }
    public bool? LastActive { get; private set; }
    public IReadOnlyList<NightLightSnapshot>? LastRestored { get; private set; }

    public IReadOnlyList<NightLightSnapshot> SnapshotAll()
    {
        SnapshotCalls++;
        return Snapshots;
    }

    public NightLightWriteResult SetActive(bool active)
    {
        SetActiveCalls++;
        LastActive = active;
        return new NightLightWriteResult(WriteResult, WriteResult ? "ok" : "failed", Array.Empty<string>());
    }

    public NightLightWriteResult Restore(IReadOnlyList<NightLightSnapshot> snapshots)
    {
        RestoreCalls++;
        LastRestored = snapshots;
        return new NightLightWriteResult(WriteResult, WriteResult ? "restored" : "failed", Array.Empty<string>());
    }
}

internal sealed class FakeJournalStore : IJournalStore
{
    public JournalRecord? Record { get; set; }
    public int BeginCalls { get; private set; }
    public int ClearCalls { get; private set; }
    public IReadOnlyList<NightLightSnapshot>? LastBegin { get; private set; }

    public void BeginApply(string reason, IReadOnlyList<NightLightSnapshot>? nightLight)
    {
        BeginCalls++;
        LastBegin = nightLight;
        Record = new JournalRecord(true, reason, DateTimeOffset.Now.ToString("o"), null);
    }

    public JournalRecord? Read() => Record;

    public void Clear()
    {
        ClearCalls++;
        Record = null;
    }
}

/// <summary>A ready-to-use presentation manager plus the fakes it was built from.</summary>
internal sealed class PresentationHarness : IDisposable
{
    public PresentationHarness(DisplayTopology topology, DpiScale? dpi = null)
    {
        Topology.Topology = topology;
        Dpi.ReadResult = dpi;
        Session = new SessionManager();
        Manager = new PresentationManager(
            Changes,
            Topology,
            Mode,
            Dpi,
            NightLight,
            Journal,
            Session);
    }

    public FakeChangeSource Changes { get; } = new();
    public FakeTopologySource Topology { get; } = new();
    public FakeModeController Mode { get; } = new();
    public FakeDpiController Dpi { get; } = new();
    public FakeNightLightController NightLight { get; } = new();
    public FakeJournalStore Journal { get; } = new();
    public SessionManager Session { get; }
    public PresentationManager Manager { get; }

    public void Dispose() => Manager.Dispose();
}

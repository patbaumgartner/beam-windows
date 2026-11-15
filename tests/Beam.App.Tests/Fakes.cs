using Beam.App.Journal;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Ports;
using Beam.Core.Settings;
using Beam.Core.Status;

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

    /// <summary>Mirrored topology after a successful fix: 16:9 shared desktop, no mismatch.</summary>
    public static DisplayTopology CloneFixed16x9() => new(
        new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 1920, 1080, 1920, 1080),
            Path(1, 2, "DELL", isInternal: false, active: true, 1920, 1080, 1920, 1080),
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

    /// <summary>Optional hook invoked at the start of every call (kill-injection in soak tests).</summary>
    public Action? BeforeCall { get; set; }

    public DisplayTopology Query()
    {
        BeforeCall?.Invoke();
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

    /// <summary>Hook run after a successful apply; tests use it to flip the fake topology.</summary>
    public Action? OnApply { get; set; }

    /// <summary>Optional hook invoked at the start of every call (kill-injection in soak tests).</summary>
    public Action? BeforeCall { get; set; }

    /// <summary>C1: the last <c>preferNative</c> value passed in (tests can pin it).</summary>
    public bool? LastPreferNative { get; private set; }

    public string? ApplyCloneWith16x9(bool preferNative = true)
    {
        BeforeCall?.Invoke();
        ApplyCalls++;
        LastPreferNative = preferNative;
        if (ApplyResult is null)
        {
            OnApply?.Invoke();
        }

        return ApplyResult;
    }

    public string? Restore()
    {
        BeforeCall?.Invoke();
        RestoreCalls++;
        return RestoreResult;
    }
}

internal sealed class FakeDpiController : IDpiController
{
    public DpiScale? ReadResult { get; set; }
    public bool SetResult { get; set; } = true;
    public List<int> RequestedPercents { get; } = new();

    /// <summary>Optional hook invoked at the start of every call (kill-injection in soak tests).</summary>
    public Action? BeforeCall { get; set; }

    public DpiScale? Read(DisplayPath path)
    {
        BeforeCall?.Invoke();
        return ReadResult;
    }

    public bool SetPercentageAndVerify(DisplayPath path, int percent, DpiScaleValue? current = null)
    {
        BeforeCall?.Invoke();
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

    /// <summary>Optional hook invoked at the start of every call (kill-injection in soak tests).</summary>
    public Action? BeforeCall { get; set; }

    public IReadOnlyList<NightLightSnapshot> SnapshotAll()
    {
        BeforeCall?.Invoke();
        SnapshotCalls++;
        return Snapshots;
    }

    public NightLightWriteResult SetActive(bool active)
    {
        BeforeCall?.Invoke();
        SetActiveCalls++;
        LastActive = active;
        return new NightLightWriteResult(WriteResult, WriteResult ? "ok" : "failed", Array.Empty<string>());
    }

    public NightLightWriteResult Restore(IReadOnlyList<NightLightSnapshot> snapshots)
    {
        BeforeCall?.Invoke();
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
    public IReadOnlyList<JournalDpiEntry>? LastDpi { get; private set; }
    public JournalExtras? LastExtras { get; private set; }

    /// <summary>Optional hook invoked at the start of every call (kill-injection in soak tests).</summary>
    public Action? BeforeCall { get; set; }

    public void BeginApply(
        string reason,
        IReadOnlyList<NightLightSnapshot>? nightLight,
        IReadOnlyList<JournalDpiEntry>? dpi = null,
        JournalExtras? extras = null)
    {
        BeforeCall?.Invoke();
        BeginCalls++;
        LastBegin = nightLight;
        LastDpi = dpi;
        LastExtras = extras;
        Record = new JournalRecord(
            true, reason, DateTimeOffset.Now.ToString("o"), null, dpi, JournalRecord.CurrentVersion, extras);
    }

    public JournalRecord? Read()
    {
        BeforeCall?.Invoke();
        return Record;
    }

    public void Clear()
    {
        BeforeCall?.Invoke();
        ClearCalls++;
        Record = null;
    }
}

/// <summary>B3 fake keep-awake port: records Hold/Release calls.</summary>
internal sealed class FakeKeepAwake : IKeepAwake
{
    public int HoldCalls { get; private set; }
    public int ReleaseCalls { get; private set; }

    public void Hold() => HoldCalls++;

    public void Release() => ReleaseCalls++;
}

/// <summary>B3 fake DND/toasts port: configurable snapshot result, records writes.</summary>
internal sealed class FakeDndController : IDndController
{
    public bool? SnapshotResult { get; set; } = true;
    public bool SetResult { get; set; } = true;
    public bool ThrowOnSet { get; set; }
    public int SnapshotCalls { get; private set; }
    public List<bool> Requested { get; } = new();

    public bool? SnapshotToastsEnabled()
    {
        SnapshotCalls++;
        return SnapshotResult;
    }

    public bool SetToastsEnabled(bool enabled)
    {
        if (ThrowOnSet)
        {
            throw new InvalidOperationException("simulated DND failure");
        }

        Requested.Add(enabled);
        return SetResult;
    }
}

/// <summary>B3 fake audio port: configurable endpoints, records switches/restores.</summary>
internal sealed class FakeAudioController : IAudioController
{
    public string? DefaultEndpoint { get; set; } = "prev-speakers";
    public string? HdmiEndpoint { get; set; } = "hdmi-projector";
    public bool RestoreResult { get; set; } = true;
    public bool ThrowOnSwitch { get; set; }
    public int GetDefaultCalls { get; private set; }
    public int SwitchCalls { get; private set; }
    public List<string> Restored { get; } = new();

    public string? GetDefaultRenderEndpoint()
    {
        GetDefaultCalls++;
        return DefaultEndpoint;
    }

    public string? SwitchToHdmi()
    {
        SwitchCalls++;
        if (ThrowOnSwitch)
        {
            throw new InvalidOperationException("simulated audio failure");
        }

        return HdmiEndpoint;
    }

    public bool Restore(string endpointId)
    {
        Restored.Add(endpointId);
        return RestoreResult;
    }
}

/// <summary>B6 fake status writer: records every published status record.</summary>
internal sealed class FakeStatusWriter : IStatusWriter
{
    public List<StatusFileRecord> Writes { get; } = new();
    public int ClearCalls { get; private set; }

    public void Write(StatusFileRecord record) => Writes.Add(record);

    public void Clear() => ClearCalls++;
}

/// <summary>B4 fake settings store: in-memory, records load/save calls.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    public BeamSettings Current { get; private set; } = BeamSettings.Defaults;
    public int LoadCalls;
    public int SaveCalls;

    public event Action<string>? CorruptFileSetAside;

    public BeamSettings Load()
    {
        LoadCalls++;
        return Current;
    }

    public void Save(BeamSettings settings)
    {
        SaveCalls++;
        Current = settings;
    }

    public void RaiseCorrupt(string path) => CorruptFileSetAside?.Invoke(path);
}

/// <summary>A ready-to-use presentation manager plus the fakes it was built from.</summary>
internal sealed class PresentationHarness : IDisposable
{
    public PresentationHarness(
        DisplayTopology topology,
        DpiScale? dpi = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? selfChangeWindow = null,
        ISettingsStore? settings = null,
        bool wireExtras = true,
        bool wireStatus = true,
        TimeSpan? verifySettleInterval = null,
        int? verifyMaxAttempts = null)
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
            Session,
            selfChangeWindow: selfChangeWindow,
            clock: clock,
            settings: settings,
            keepAwake: wireExtras ? KeepAwake : null,
            dnd: wireExtras ? Dnd : null,
            audio: wireExtras ? Audio : null,
            statusWriter: wireStatus ? Status : null,
            verifySettleInterval: verifySettleInterval,
            verifyMaxAttempts: verifyMaxAttempts);

        // A successful mode apply resolves the mismatch: the fake topology flips to the
        // fixed 16:9 clone so the manager's verify-after-mode step can observe it.
        Mode.OnApply = () => Topology.Topology = Topologies.CloneFixed16x9();
    }

    public FakeChangeSource Changes { get; } = new();
    public FakeTopologySource Topology { get; } = new();
    public FakeModeController Mode { get; } = new();
    public FakeDpiController Dpi { get; } = new();
    public FakeNightLightController NightLight { get; } = new();
    public FakeJournalStore Journal { get; } = new();
    public FakeKeepAwake KeepAwake { get; } = new();
    public FakeDndController Dnd { get; } = new();
    public FakeAudioController Audio { get; } = new();
    public FakeStatusWriter Status { get; } = new();
    public SessionManager Session { get; }
    public PresentationManager Manager { get; }

    public void Dispose() => Manager.Dispose();
}

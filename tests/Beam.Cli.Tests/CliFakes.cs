using Beam.App.Journal;
using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Ports;
using Beam.Core.Status;

namespace Beam.Cli.Tests;

internal static class CliTopologies
{
    public static DisplayPath Path(
        uint sourceId,
        uint targetId,
        string name,
        bool isInternal,
        uint desktopW,
        uint desktopH,
        uint signalW,
        uint signalH,
        bool active = true,
        bool withModes = true) =>
        new(
            $@"\\.\DISPLAY{sourceId + 1}",
            sourceId,
            "0001594D:00000000",
            targetId,
            new DisplayTarget(name, "DEL", 0x1234, "HDMI", $@"monitor\{name}", isInternal, true),
            active,
            withModes ? new DisplayMode(desktopW, desktopH, 60) : null,
            withModes ? new DisplayMode(signalW, signalH, 60) : null,
            0, 0, "0", null, null, null, null);

    public static DisplayTopology Mirrored() => new(
        new[]
        {
            Path(0, 1, "Laptop", isInternal: true, 1920, 1080, 1920, 1080),
            Path(1, 2, "DELL", isInternal: false, 1920, 1080, 1920, 1200),
        },
        DisplayTopologyId.Clone);

    public static DisplayTopology Extended() => new(
        new[]
        {
            Path(0, 1, "Laptop", isInternal: true, 1920, 1080, 1920, 1080),
            Path(1, 2, "DELL", isInternal: false, 1920, 1080, 1920, 1080),
        },
        DisplayTopologyId.Extend);

    /// <summary>
    /// Mirrored with an aspect mismatch (DELL 1920x1200 vs shared 1920x1080), but the
    /// first active path has no source/target mode — the CLI must render it as 0x0.
    /// </summary>
    public static DisplayTopology MirroredWithModelessFirstPath() => new(
        new[]
        {
            Path(0, 1, "Ghost", isInternal: false, 1920, 1080, 1920, 1080, withModes: false),
            Path(1, 2, "DELL", isInternal: false, 1920, 1080, 1920, 1200),
            Path(2, 3, "Laptop", isInternal: true, 1920, 1080, 1920, 1080),
        },
        DisplayTopologyId.Clone);

    /// <summary>One active display plus two stale (inactive) known targets.</summary>
    public static DisplayTopology OneActivePlusStale() => new(
        new[]
        {
            Path(0, 1, "Laptop", isInternal: true, 1920, 1080, 1920, 1080),
            Path(1, 2, "DELL", isInternal: false, 1920, 1080, 1920, 1080, active: false),
            Path(2, 3, "OldBeamer", isInternal: false, 1280, 720, 1280, 720, active: false),
        },
        DisplayTopologyId.Extend);
}

internal sealed class CliTopologySource : ITopologySource
{
    public DisplayTopology Topology { get; set; } = DisplayTopology.Empty;

    public DisplayTopology Query() => Topology;
}

internal sealed class CliModeController : IDisplayModeController
{
    public string? ApplyResult { get; set; }
    public string? RestoreResult { get; set; }
    public int ApplyCalls { get; private set; }
    public int RestoreCalls { get; private set; }

    public string? ApplyCloneWith16x9(bool preferNative = true)
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

internal sealed class CliDpiController : IDpiController
{
    public DpiScale? ReadResult { get; set; }
    public bool SetResult { get; set; } = true;
    public List<int> RequestedPercents { get; } = new();
    public DisplayPath? LastReadPath { get; private set; }
    public DisplayPath? LastSetPath { get; private set; }

    public DpiScale? Read(DisplayPath path)
    {
        LastReadPath = path;
        return ReadResult;
    }

    public bool SetPercentageAndVerify(DisplayPath path, int percent, DpiScaleValue? current = null)
    {
        LastSetPath = path;
        RequestedPercents.Add(percent);
        return SetResult;
    }
}

internal sealed class CliNightLightController : INightLightController
{
    public bool WriteResult { get; set; } = true;
    public int SetActiveCalls { get; private set; }
    public int RestoreCalls { get; private set; }
    public bool? LastActive { get; private set; }
    public IReadOnlyList<NightLightSnapshot>? LastRestored { get; private set; }

    public IReadOnlyList<NightLightSnapshot> SnapshotAll() => Array.Empty<NightLightSnapshot>();

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

internal sealed class CliJournalStore : IJournalStore
{
    public JournalRecord? Record { get; set; }
    public int BeginCalls { get; private set; }
    public int ClearCalls { get; private set; }
    public IReadOnlyList<JournalDpiEntry>? LastDpi { get; private set; }
    public JournalExtras? LastExtras { get; private set; }

    public void BeginApply(
        string reason,
        IReadOnlyList<NightLightSnapshot>? nightLight,
        IReadOnlyList<JournalDpiEntry>? dpi = null,
        JournalExtras? extras = null)
    {
        BeginCalls++;
        LastDpi = dpi;
        LastExtras = extras;
        Record = new JournalRecord(
            true, reason, DateTimeOffset.Now.ToString("o"), null, dpi, JournalRecord.CurrentVersion, extras);
    }

    public JournalRecord? Read() => Record;

    public void Clear()
    {
        ClearCalls++;
        Record = null;
    }
}

/// <summary>A CLI wired to fakes, with captured stdout/stderr.</summary>
internal sealed class CliHarness
{
    public CliHarness(DisplayTopology topology)
    {
        Topology.Topology = topology;
    }

    public CliTopologySource Topology { get; } = new();
    public CliModeController Mode { get; } = new();
    public CliDpiController Dpi { get; } = new();
    public CliNightLightController NightLight { get; } = new();
    public CliJournalStore Journal { get; } = new();
    public StringWriter Out { get; } = new();
    public StringWriter Error { get; } = new();

    /// <summary>
    /// Lock factory injected into <see cref="CliApp"/>. Defaults to a no-op lease so
    /// parallel tests never contend on the real Local\Beam.SingleInstance mutex;
    /// the A1 lock tests replace it with a counting/failing factory.
    /// </summary>
    public Func<IDisposable?>? LockFactory { get; set; } = () => NoopLock.Instance;

    /// <summary>B6: status-file reader injected into <see cref="CliApp"/> (null = no tray status).</summary>
    public Func<StatusFileRecord?>? StatusReader { get; set; }

    public CliApp App => new(Topology, Mode, Dpi, NightLight, Journal, Out, Error, LockFactory, StatusReader);

    public int Run(params string[] args) => App.Run(args);

    public string Output => Out.ToString();
    public string Errors => Error.ToString();
}

/// <summary>No-op lock lease for CLI tests that do not exercise the single-instance lock.</summary>
internal sealed class NoopLock : IDisposable
{
    public static readonly NoopLock Instance = new();

    public void Dispose()
    {
    }
}

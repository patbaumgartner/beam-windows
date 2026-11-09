using Beam.App.Journal;
using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Ports;

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
        uint signalH) =>
        new(
            $@"\\.\DISPLAY{sourceId + 1}",
            sourceId,
            "0001594D:00000000",
            targetId,
            new DisplayTarget(name, "DEL", 0x1234, "HDMI", $@"monitor\{name}", isInternal, true),
            true,
            new DisplayMode(desktopW, desktopH, 60),
            new DisplayMode(signalW, signalH, 60),
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

internal sealed class CliDpiController : IDpiController
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

    public void BeginApply(
        string reason,
        IReadOnlyList<NightLightSnapshot>? nightLight,
        IReadOnlyList<JournalDpiEntry>? dpi = null)
    {
        BeginCalls++;
        LastDpi = dpi;
        Record = new JournalRecord(true, reason, DateTimeOffset.Now.ToString("o"), null, dpi);
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

    public CliApp App => new(Topology, Mode, Dpi, NightLight, Journal, Out, Error);

    public int Run(params string[] args) => App.Run(args);

    public string Output => Out.ToString();
    public string Errors => Error.ToString();
}

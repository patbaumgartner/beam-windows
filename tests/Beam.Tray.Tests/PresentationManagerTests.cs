using Beam.Core.Display;

namespace Beam.Tray.Tests;

public class PresentationManagerTests
{
    private static DisplayPath Path(
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

    [Fact]
    public void Trigger_False_WhenNotMirrored()
    {
        var topology = new DisplayTopology(new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 3840, 2400, 3840, 2400),
            Path(1, 2, "DELL", isInternal: false, active: true, 3840, 2400, 3840, 2160),
        }, DisplayTopologyId.Extend);

        Assert.False(PresentationManager.IsTriggerConditionMet(topology));
    }

    [Fact]
    public void Trigger_True_ConfirmedMirroredWithMismatch()
    {
        // Two-source duplicate on this machine: both sources share the 16:10 desktop
        // (3840x2400); the DELL's signal is 16:9 (3840x2160) -> side bars to fix.
        var topology = new DisplayTopology(new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 3840, 2400, 3840, 2400),
            Path(1, 2, "DELL", isInternal: false, active: true, 3840, 2400, 3840, 2160),
        }, DisplayTopologyId.Clone);

        Assert.True(PresentationManager.IsTriggerConditionMet(topology));
    }

    [Fact]
    public void Trigger_True_AfterFix_NoMismatch()
    {
        // After Beam applies the fix, the shared desktop is 16:9 and both signals are
        // 16:9 — no bars, so the trigger should NOT fire (and Beam must not re-fix).
        var topology = new DisplayTopology(new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 3840, 2160, 3840, 2160),
            Path(1, 2, "DELL", isInternal: false, active: true, 3840, 2160, 3840, 2160),
        }, DisplayTopologyId.Clone);

        Assert.False(PresentationManager.IsTriggerConditionMet(topology));
    }

    [Fact]
    public void Trigger_False_WhenAspectMatched()
    {
        var topology = new DisplayTopology(new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 1920, 1080, 1920, 1080),
            Path(1, 2, "DELL", isInternal: false, active: true, 1920, 1080, 1920, 1080),
        }, DisplayTopologyId.Clone);

        Assert.False(PresentationManager.IsTriggerConditionMet(topology));
    }

    [Fact]
    public void Trigger_False_WhenNoExternal()
    {
        var topology = new DisplayTopology(new[]
        {
            Path(0, 1, "Laptop", isInternal: true, active: true, 3840, 2400, 3840, 2400),
        }, DisplayTopologyId.Internal);

        Assert.False(PresentationManager.IsTriggerConditionMet(topology));
    }
}

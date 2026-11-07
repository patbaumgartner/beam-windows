using Beam.Core.Display;

namespace Beam.Cli.Tests;

/// <summary>
/// The "no monitor connected" contract: with an empty (or laptop-only) topology every
/// command must degrade gracefully — sensible exit codes, no crashes, helpful messages.
/// This is the UX a user gets when Beam runs on a machine with zero external displays.
/// </summary>
public class NoMonitorConnectedTests
{
    /// <summary>No displays at all — the headless/docked-away case.</summary>
    private static readonly DisplayTopology ZeroDisplays =
        new(Array.Empty<DisplayPath>(), 0);

    /// <summary>Only the laptop's internal panel, extended topology.</summary>
    private static readonly DisplayTopology InternalOnly = new(
        new[]
        {
            CliTopologies.Path(0, 1, "Laptop", isInternal: true, 1920, 1080, 1920, 1080),
        },
        DisplayTopologyId.Internal);

    [Fact]
    public void Status_NoDisplays_ReportsCalmState()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("status");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("Mirrored: no", h.Output);
        Assert.Contains("Needs fix: no", h.Output);
    }

    [Fact]
    public void Status_NoDisplays_Json_WellFormed()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("status", "--json");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("\"needsFix\":false", h.Output);
        Assert.Contains("\"mirrored\":false", h.Output);
    }

    [Fact]
    public void Status_InternalOnly_ReportsNoFix()
    {
        var h = new CliHarness(InternalOnly);

        int code = h.Run("status");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("Laptop", h.Output);
        Assert.Contains("Needs fix: no", h.Output);
    }

    [Fact]
    public void Devices_NoDisplays_EmptyList_Success()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("devices");

        Assert.Equal(ExitCodes.Success, code);
        Assert.DoesNotContain("[active]", h.Output);
    }

    [Fact]
    public void Apply_NoDisplays_ExitsNoProjector()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("apply");

        Assert.Equal(ExitCodes.NoProjector, code);
        Assert.Contains("No projector detected", h.Errors);
    }

    [Fact]
    public void Apply_InternalOnly_ExitsNoProjector()
    {
        var h = new CliHarness(InternalOnly);

        int code = h.Run("apply");

        Assert.Equal(ExitCodes.NoProjector, code);
    }

    [Fact]
    public void Apply_NoDisplays_DryRun_StillNoProjector()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("apply", "--dry-run");

        Assert.Equal(ExitCodes.NoProjector, code);
        // Dry run with nothing connected must NOT print a plan.
        Assert.DoesNotContain("Dry run", h.Output);
    }

    [Fact]
    public void Apply_InternalOnly_DryRun_StillNoProjector()
    {
        var h = new CliHarness(InternalOnly);

        int code = h.Run("apply", "--dry-run");

        Assert.Equal(ExitCodes.NoProjector, code);
    }

    [Fact]
    public void Restore_NoDisplays_RunsAndSucceeds()
    {
        // Restore with nothing connected is a no-op that must still succeed: the user
        // may have already unplugged the projector before getting to run restore.
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("restore");

        Assert.Equal(ExitCodes.Success, code);
    }

    [Fact]
    public void Restore_InternalOnly_Succeeds()
    {
        var h = new CliHarness(InternalOnly);

        int code = h.Run("restore");

        Assert.Equal(ExitCodes.Success, code);
    }

    [Fact]
    public void Restore_NoJournal_NightLightUnchanged()
    {
        var h = new CliHarness(ZeroDisplays);

        h.Run("restore");

        Assert.Equal(0, h.NightLight.SetActiveCalls);
        Assert.Contains("Night Light: Night Light left unchanged.", h.Output);
    }

    [Fact]
    public void Restore_WithNightLightFlag_ForcesNightLightOn()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("restore", "--nightlight");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(1, h.NightLight.SetActiveCalls);
        Assert.True(h.NightLight.LastActive);
    }

    [Fact]
    public void DpiRead_NoDisplays_FailsGracefully()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("dpi", "read");

        Assert.Equal(ExitCodes.Failure, code);
        Assert.Contains("No active display", h.Errors);
    }

    [Fact]
    public void DpiRead_InternalOnly_TargetsInternalPanel()
    {
        var h = new CliHarness(InternalOnly);
        h.Dpi.ReadResult = DpiScale.From(-4, 0, 4);

        int code = h.Run("dpi", "read");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("DPI current=200%", h.Output);
    }

    [Fact]
    public void DpiSet_NoDisplays_FailsGracefully()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("dpi", "set", "150");

        Assert.Equal(ExitCodes.Failure, code);
        Assert.Contains("No active display", h.Errors);
    }

    [Fact]
    public void NightLight_NoDisplays_WritesUnchanged()
    {
        // Night Light is not display-bound: it must work with zero displays.
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("nightlight", "off");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(1, h.NightLight.SetActiveCalls);
    }

    [Fact]
    public void Help_NoDisplays_Works()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("help");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("beam status", h.Output);
    }

    [Fact]
    public void Version_Works()
    {
        var h = new CliHarness(ZeroDisplays);

        int code = h.Run("help", "--json");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("\"version\":", h.Output);
    }
}

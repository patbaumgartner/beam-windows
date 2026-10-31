using System.Text.Json;
using Beam.App.Journal;
using Beam.Core.Display;

namespace Beam.Cli.Tests;

public class CliAppTests
{
    private static readonly byte[] RealDefaultOff =
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06,
        0x86, 0xE6, 0x8B, 0xD7, 0x06,
        0x2A, 0x2B, 0x0E, 0x10, 0x43, 0x42, 0x01,
        0x00, 0xC6, 0x14,
        0xAA, 0x8E, 0xD0, 0xE6, 0xD8, 0xEE, 0xD9, 0xEE, 0x01,
        0x00, 0x00, 0x00, 0x00,
    ];

    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    // ── Argument handling ───────────────────────────────────────────────────────

    [Fact]
    public void NoArguments_PrintsHelp_AndReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run();

        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Contains("Usage:", h.Output);
    }

    [Fact]
    public void Help_ReturnsSuccess()
    {
        var h = new CliHarness(CliTopologies.Extended());

        Assert.Equal(ExitCodes.Success, h.Run("help"));
        Assert.Contains("beam status", h.Output);
    }

    [Fact]
    public void UnknownCommand_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("frobnicate");

        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Contains("Unknown command", h.Errors);
    }

    [Fact]
    public void UnknownOption_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("status", "--wat");

        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Contains("Unknown option", h.Errors);
    }

    [Fact]
    public void DeviceWithoutValue_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("status", "--device");

        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Contains("--device requires", h.Errors);
    }

    [Fact]
    public void Parser_CollectsFlagsAndPositionals()
    {
        CliOptions options = CommandLineParser.Parse(
            "dpi",
            new[] { "set", "150", "--device", @"\\.\DISPLAY2", "--json", "--verify" });

        Assert.Equal("dpi", options.Command);
        Assert.Equal(new[] { "set", "150" }, options.Positionals);
        Assert.Equal(@"\\.\DISPLAY2", options.Device);
        Assert.True(options.Json);
        Assert.True(options.Verify);
    }

    // ── status / devices ────────────────────────────────────────────────────────

    [Fact]
    public void Status_ReportsTrigger()
    {
        var h = new CliHarness(CliTopologies.Mirrored());

        int code = h.Run("status");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("Needs fix: YES", h.Output);
        Assert.Contains("DELL", h.Output);
    }

    [Fact]
    public void Status_Json_IsParseable()
    {
        var h = new CliHarness(CliTopologies.Mirrored());

        int code = h.Run("status", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.True(doc.RootElement.GetProperty("needsFix").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("mirrored").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("displays").GetArrayLength());
    }

    [Fact]
    public void Devices_ListsAllPaths()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("devices", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.Equal(2, doc.RootElement.GetProperty("devices").GetArrayLength());
    }

    // ── apply / restore ────────────────────────────────────────────────────────

    [Fact]
    public void Apply_WithoutProjector_ReturnsNoProjector()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("apply");

        Assert.Equal(ExitCodes.NoProjector, code);
        Assert.Equal(0, h.Mode.ApplyCalls);
    }

    [Fact]
    public void Apply_DryRun_DoesNotChangeAnything()
    {
        var h = new CliHarness(CliTopologies.Mirrored());

        int code = h.Run("apply", "--dry-run");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(0, h.Mode.ApplyCalls);
        Assert.Equal(0, h.NightLight.SetActiveCalls);
        Assert.Contains("Dry run", h.Output);
    }

    [Fact]
    public void Apply_AppliesModeDpiAndNightLight_AndWritesJournal()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.Dpi.ReadResult = ScaleAt200;

        int code = h.Run("apply");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(1, h.NightLight.SetActiveCalls);
        Assert.False(h.NightLight.LastActive);
        Assert.Equal(1, h.Journal.BeginCalls);
        Assert.Contains(100, h.Dpi.RequestedPercents);
    }

    [Fact]
    public void Apply_ModeFailure_ReturnsFailure()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.Mode.ApplyResult = "rejected";

        int code = h.Run("apply");

        Assert.Equal(ExitCodes.Failure, code);
    }

    [Fact]
    public void Restore_WithJournal_RestoresCapturedNightLight()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.Journal.Record = new JournalRecord(
            true,
            "cli-apply",
            DateTimeOffset.Now.ToString("o"),
            new[] { new JournalEntry(@"KEY\default", "Data", Convert.ToBase64String(RealDefaultOff)) });

        int code = h.Run("restore");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(1, h.NightLight.RestoreCalls);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    [Fact]
    public void Restore_WithoutJournal_LeavesNightLightAlone()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("restore");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(0, h.NightLight.SetActiveCalls);
        Assert.Contains("left unchanged", h.Output);
    }

    [Fact]
    public void Restore_NightLightFlag_TurnsItOn()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("restore", "--nightlight");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(1, h.NightLight.SetActiveCalls);
        Assert.True(h.NightLight.LastActive);
    }

    // ── dpi ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DpiRead_ReportsCurrentScale()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.Dpi.ReadResult = ScaleAt200;

        int code = h.Run("dpi", "read");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("current=200%", h.Output);
    }

    [Fact]
    public void DpiRead_Unsupported_ReturnsFailure()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.Dpi.ReadResult = null;

        int code = h.Run("dpi", "read");

        Assert.Equal(ExitCodes.Failure, code);
    }

    [Fact]
    public void DpiRead_UnknownDevice_ReturnsFailure()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.Dpi.ReadResult = ScaleAt200;

        int code = h.Run("dpi", "read", "--device", @"\\.\DISPLAY9");

        Assert.Equal(ExitCodes.Failure, code);
        Assert.Contains("No active display matching", h.Errors);
    }

    [Fact]
    public void DpiSet_AppliesAndReturnsSuccess()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("dpi", "set", "150");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(new[] { 150 }, h.Dpi.RequestedPercents);
    }

    [Fact]
    public void DpiSet_MissingPercent_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("dpi", "set");

        Assert.Equal(ExitCodes.InvalidArguments, code);
    }

    [Fact]
    public void DpiSet_InvalidPercent_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("dpi", "set", "lots");

        Assert.Equal(ExitCodes.InvalidArguments, code);
    }

    [Fact]
    public void Dpi_UnknownAction_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("dpi", "wobble");

        Assert.Equal(ExitCodes.InvalidArguments, code);
    }

    // ── nightlight ──────────────────────────────────────────────────────────────

    [Fact]
    public void NightLight_Off_TurnsItOff()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("nightlight", "off");

        Assert.Equal(ExitCodes.Success, code);
        Assert.False(h.NightLight.LastActive);
    }

    [Fact]
    public void NightLight_Invalid_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("nightlight", "maybe");

        Assert.Equal(ExitCodes.InvalidArguments, code);
    }
}

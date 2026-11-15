using System.Text.Json;
using Beam.App.Journal;
using Beam.Core.Display;
using Beam.Core.Status;

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
        Assert.Equal("0.2.0", doc.RootElement.GetProperty("version").GetString());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("needsFix").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("mirrored").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("displays").GetArrayLength());
        Assert.DoesNotContain('\n', h.Output.TrimEnd('\r', '\n')); // --json stays one compact line
    }

    [Fact]
    public void Status_Json_PathWithoutMode_ReportsZeroByZero()
    {
        var h = new CliHarness(new DisplayTopology(
            new[]
            {
                CliTopologies.Path(0, 1, "Laptop", isInternal: true, 1920, 1080, 1920, 1080,
                    withModes: false),
            },
            DisplayTopologyId.Extend));

        int code = h.Run("status", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        JsonElement display = doc.RootElement.GetProperty("displays")[0];
        Assert.Equal("0x0", display.GetProperty("source").GetString());
        Assert.Equal("0x0", display.GetProperty("signal").GetString());
    }

    // ── B6: live tray session block ───────────────────────────────────────────

    [Fact]
    public void Status_Json_IncludesSessionBlock_WhenTrayStatusPresent()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.StatusReader = () => new StatusFileRecord(
            "Active", "fix sequence complete", "2026-11-14T10:00:00+00:00", 4321,
            "1920x1080@60", 100, true);

        int code = h.Run("status", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        JsonElement session = doc.RootElement.GetProperty("session");
        Assert.Equal(JsonValueKind.Object, session.ValueKind);
        Assert.Equal("Active", session.GetProperty("state").GetString());
        Assert.Equal("fix sequence complete", session.GetProperty("reason").GetString());
        Assert.Equal(4321, session.GetProperty("pid").GetInt32());
        Assert.Equal("1920x1080@60", session.GetProperty("appliedSourceMode").GetString());
        Assert.Equal(100, session.GetProperty("appliedDpiPercent").GetInt32());
        Assert.True(session.GetProperty("nightLightDisabled").GetBoolean());
    }

    [Fact]
    public void Status_Text_IncludesSessionLine_WhenTrayStatusPresent()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.StatusReader = () => new StatusFileRecord(
            "Active", "fix sequence complete", "2026-11-14T10:00:00+00:00", 4321,
            "1920x1080@60", 100, true);

        int code = h.Run("status");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("Session: Active", h.Output);
        Assert.Contains("applied=1920x1080@60", h.Output);
    }

    [Fact]
    public void Status_Json_SessionIsNull_WhenNoTrayRunning()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.StatusReader = () => null;

        int code = h.Run("status", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("session").ValueKind);
    }

    [Fact]
    public void Status_NoSessionLine_WhenReaderAbsent()
    {
        var h = new CliHarness(CliTopologies.Mirrored());

        int code = h.Run("status");

        Assert.Equal(ExitCodes.Success, code);
        Assert.DoesNotContain("Session:", h.Output);
    }

    [Fact]
    public void Status_ThrowingReader_StillSucceeds()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.StatusReader = () => throw new InvalidOperationException("simulated read failure");

        int code = h.Run("status", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("session").ValueKind);
    }

    [Fact]
    public void Devices_ListsAllPaths()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("devices", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("devices").GetArrayLength());
    }

    [Fact]
    public void Help_Json_ReportsSuccessAndCommandList()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("help", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("apply", doc.RootElement.GetProperty("commands").EnumerateArray()
            .Select(c => c.GetString()));
    }

    [Fact]
    public void ExitCodes_MatchTheDocumentedContract()
    {
        // beam help promises: 0 success, 1 failure, 2 bad arguments, 3 no projector, 4 unsupported.
        Assert.Equal(0, ExitCodes.Success);
        Assert.Equal(1, ExitCodes.Failure);
        Assert.Equal(2, ExitCodes.InvalidArguments);
        Assert.Equal(3, ExitCodes.NoProjector);
        Assert.Equal(4, ExitCodes.Unsupported);
    }

    [Fact]
    public void Devices_Text_ShowsActiveRows_AndSummarizesInactive()
    {
        var h = new CliHarness(CliTopologies.OneActivePlusStale());

        int code = h.Run("devices");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("[active]", h.Output);
        Assert.Contains("Laptop", h.Output);
        Assert.DoesNotContain("[inactive]", h.Output);
        Assert.DoesNotContain("DELL", h.Output); // stale target is summarized, not listed
        Assert.Contains("2 more known outputs are inactive", h.Output);
    }

    [Fact]
    public void Devices_Text_SingularInactive_UsesSingularWording()
    {
        var h = new CliHarness(new DisplayTopology(
            new[]
            {
                CliTopologies.Path(0, 1, "Laptop", isInternal: true, 1920, 1080, 1920, 1080),
                CliTopologies.Path(1, 2, "DELL", isInternal: false, 1920, 1080, 1920, 1080, active: false),
            },
            DisplayTopologyId.Extend));

        int code = h.Run("devices");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("1 more known output is inactive", h.Output);
    }

    [Fact]
    public void Devices_Text_NoDisplays_SaysSo()
    {
        var h = new CliHarness(DisplayTopology.Empty);

        int code = h.Run("devices");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("No displays detected.", h.Output);
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
    public void Apply_WithoutProjector_Json_ReportsFailure()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("apply", "--json");

        Assert.Equal(ExitCodes.NoProjector, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("No projector", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Apply_DryRun_Json_ReportsThePlan()
    {
        var h = new CliHarness(CliTopologies.Mirrored());

        int code = h.Run("apply", "--dry-run", "--json");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(0, h.Mode.ApplyCalls);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("duplicate", doc.RootElement.GetProperty("plan").GetProperty("action").GetString());
    }

    [Fact]
    public void Apply_DryRun_Json_PlanRendersModelessPathAsZeroByZero()
    {
        var h = new CliHarness(CliTopologies.MirroredWithModelessFirstPath());

        int code = h.Run("apply", "--dry-run", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.Contains(":0x0", doc.RootElement.GetProperty("plan").GetProperty("before").GetString());
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
    public void Restore_Json_ReportsSuccess_WhenNothingFailed()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("restore", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Restore_Json_SuccessIsFalse_WhenNightLightRestoreFails()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.NightLight.WriteResult = false;

        int code = h.Run("restore", "--nightlight", "--json");

        Assert.Equal(ExitCodes.Success, code); // mode restore itself succeeded
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
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

    // ── Roadmap 1.9: original DPI is journalled on apply and restored on restore ──

    [Fact]
    public void Apply_WritesOriginalDpiToJournal()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.Dpi.ReadResult = ScaleAt200;

        int code = h.Run("apply");

        Assert.Equal(ExitCodes.Success, code);
        JournalDpiEntry entry = Assert.Single(h.Journal.LastDpi!);
        Assert.Equal(200, entry.Percent);
        Assert.Equal("0001594D:00000000", entry.AdapterId);
    }

    [Fact]
    public void Restore_WithDpiJournal_RestoresCapturedDpi_OnTheMatchingSource()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.Journal.Record = new JournalRecord(
            true,
            "cli-apply",
            DateTimeOffset.Now.ToString("o"),
            null,
            new[] { new JournalDpiEntry("0001594D:00000000", 1, 200) });

        int code = h.Run("restore");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains(200, h.Dpi.RequestedPercents); // original scale put back
        Assert.Equal(1u, h.Dpi.LastSetPath?.SourceId); // the journalled source, not just any path
        Assert.Contains("restored to 200%", h.Output); // verified, not "NOT verified"
        Assert.Contains("DPI:", h.Output);
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    [Fact]
    public void Restore_WithoutDpiJournal_ReportsDpiUnchanged()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("restore");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Empty(h.Dpi.RequestedPercents);
        Assert.Contains("DPI:", h.Output);
        Assert.Contains("no captured scale", h.Output);
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
    public void DpiRead_Json_ReportsSuccessAndScale()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.Dpi.ReadResult = ScaleAt200;

        int code = h.Run("dpi", "read", "--json");

        Assert.Equal(ExitCodes.Success, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(200, doc.RootElement.GetProperty("current").GetInt32());
    }

    [Fact]
    public void DpiRead_PrefersTheExternalDisplay_WhenNoDeviceGiven()
    {
        var h = new CliHarness(CliTopologies.Mirrored()); // Laptop (internal) + DELL (external)
        h.Dpi.ReadResult = ScaleAt200;

        int code = h.Run("dpi", "read");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(@"\\.\DISPLAY2", h.Dpi.LastReadPath?.SourceGdiName);
    }

    [Fact]
    public void Dpi_WithoutAction_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("dpi");

        Assert.Equal(ExitCodes.InvalidArguments, code);
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

    [Fact]
    public void NightLight_WithoutArgument_ReturnsInvalidArguments()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("nightlight");

        Assert.Equal(ExitCodes.InvalidArguments, code);
    }

    // ── Phase A (A1): single-instance lock on mutating commands ─────────────────

    [Fact]
    public void MutatingCommand_AcquiresAndReleasesLock()
    {
        var h = new CliHarness(CliTopologies.Extended());
        int acquires = 0;
        bool disposed = false;
        h.LockFactory = () =>
        {
            acquires++;
            return new FakeLease(() => disposed = true);
        };

        int code = h.Run("nightlight", "off");

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(1, acquires);
        Assert.True(disposed, "the lock lease must be released after the command completes");
    }

    [Fact]
    public void MutatingCommand_WhenLockUnavailable_FailsWithoutTouchingAnything()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.LockFactory = () => null;

        int code = h.Run("apply");

        Assert.Equal(ExitCodes.Failure, code);
        Assert.Contains("single-instance", h.Errors);
        Assert.Equal(0, h.Mode.ApplyCalls);
        Assert.Equal(0, h.Journal.BeginCalls);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("devices")]
    [InlineData("help")]
    public void ReadOnlyCommands_NeverAcquireTheLock(string command)
    {
        var h = new CliHarness(CliTopologies.Extended());
        int acquires = 0;
        h.LockFactory = () =>
        {
            acquires++;
            return new FakeLease(() => { });
        };

        int code = h.Run(command);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(0, acquires);
    }

    [Fact]
    public void DpiRead_DoesNotAcquireLock_ButDpiSet_Does()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.Dpi.ReadResult = ScaleAt200;
        int acquires = 0;
        h.LockFactory = () =>
        {
            acquires++;
            return new FakeLease(() => { });
        };

        Assert.Equal(ExitCodes.Success, h.Run("dpi", "read"));
        Assert.Equal(0, acquires);

        Assert.Equal(ExitCodes.Success, h.Run("dpi", "set", "150"));
        Assert.Equal(1, acquires);
    }

    [Fact]
    public void MutatingCommands_AlwaysAcquireTheLock()
    {
        foreach (string[] args in new[]
        {
            new[] { "apply" },
            new[] { "restore" },
            new[] { "nightlight", "off" },
        })
        {
            var h = new CliHarness(CliTopologies.Mirrored());
            int acquires = 0;
            h.LockFactory = () =>
            {
                acquires++;
                return new FakeLease(() => { });
            };

            h.Run(args);

            Assert.Equal(1, acquires);
        }
    }

    [Fact]
    public void DpiSet_WithoutPercent_AcquiresLock_ThenRejectsUsage()
    {
        var h = new CliHarness(CliTopologies.Extended());
        int acquires = 0;
        h.LockFactory = () =>
        {
            acquires++;
            return new FakeLease(() => { });
        };

        int code = h.Run("dpi", "set");

        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal(1, acquires);
    }

    [Fact]
    public void LockUnavailable_Json_ReportsSuccessFalse()
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        h.LockFactory = () => null;

        int code = h.Run("apply", "--json");

        Assert.Equal(ExitCodes.Failure, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("single-instance", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(0, h.Mode.ApplyCalls);
    }

    private sealed class FakeLease : IDisposable
    {
        private readonly Action _onDispose;

        public FakeLease(Action onDispose) => _onDispose = onDispose;

        public void Dispose() => _onDispose();
    }
}

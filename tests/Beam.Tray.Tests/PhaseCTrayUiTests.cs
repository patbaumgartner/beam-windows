using System.IO.Compression;
using System.Text;
using Beam.App.Journal;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Ports;
using Beam.Core.Settings;

namespace Beam.Tray.Tests;

/// <summary>
/// C1–C5: unit tests for the Phase C tray UI logic — the pure presentation helpers
/// (C2), the externalized string table (C5), and the settings/diagnostics view models
/// (C1/C4), driven through a <see cref="PresentationManager"/> built on fake ports.
/// No WPF/WinForms controls are created here; the windows themselves are thin shells.
/// </summary>
public class PhaseCTrayUiTests
{
    // ── Minimal fake ports ───────────────────────────────────────────────────────

    private sealed class FakeChanges : IDisplayChangeSource
    {
        public event Action<ConnectionSnapshot?, ConnectionSnapshot>? Changed;
        public void Start() { }
        public void Stop() { }
        public void Raise(ConnectionSnapshot? prev, ConnectionSnapshot cur) => Changed?.Invoke(prev, cur);
    }

    private sealed class FakeTopology : ITopologySource
    {
        public DisplayTopology Topology { get; set; } = new DisplayTopology(Array.Empty<DisplayPath>(), 0);
        public DisplayTopology Query() => Topology;
    }

    private sealed class FakeMode : IDisplayModeController
    {
        public bool? LastPreferNative { get; private set; }
        public string? ApplyCloneWith16x9(bool preferNative = true)
        {
            LastPreferNative = preferNative;
            return null;
        }
        public string? ApplyExtendWith16x9(bool preferNative = true)
        {
            LastPreferNative = preferNative;
            return null;
        }
        public string? Restore() => null;
    }

    private sealed class FakeDpi : IDpiController
    {
        public DpiScale? ReadResult { get; set; }
        public DpiScale? Read(DisplayPath path) => ReadResult;
        public bool SetPercentageAndVerify(DisplayPath path, int percent, DpiScaleValue? current = null) => true;
    }

    private sealed class FakeNightLight : INightLightController
    {
        public IReadOnlyList<NightLightSnapshot> SnapshotAll() => Array.Empty<NightLightSnapshot>();
        public NightLightWriteResult SetActive(bool active) => new(true, "ok", Array.Empty<string>());
        public NightLightWriteResult Restore(IReadOnlyList<NightLightSnapshot> snapshots) => new(true, "ok", Array.Empty<string>());
    }

    private sealed class FakeJournal : IJournalStore
    {
        public void BeginApply(string reason, IReadOnlyList<NightLightSnapshot>? nightLight, IReadOnlyList<JournalDpiEntry>? dpi = null, JournalExtras? extras = null) { }
        public JournalRecord? Read() => null;
        public void Clear() { }
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public BeamSettings Current { get; private set; } = BeamSettings.Defaults;
        public int SaveCalls { get; private set; }
        public event Action<string>? CorruptFileSetAside;
        public BeamSettings Load() => Current;
        public void Save(BeamSettings settings)
        {
            SaveCalls++;
            Current = settings;
        }
        public void RaiseCorrupt(string path) => CorruptFileSetAside?.Invoke(path);
    }

    private sealed class Harness : IDisposable
    {
        public FakeSettings Settings { get; } = new();
        public SessionManager Session { get; } = new();
        public PresentationManager Manager { get; }

        public Harness()
        {
            Manager = new PresentationManager(
                new FakeChanges(), new FakeTopology(), new FakeMode(), new FakeDpi(),
                new FakeNightLight(), new FakeJournal(), Session,
                settings: Settings);
        }

        public void Dispose()
        {
            Manager.Dispose();
            Session.Dispose();
        }
    }

    // ── C2: TrayPresentation helpers (timed pause, shortcut hint) ────────────────

    [Fact]
    public void TimedPausePresets_Are15minAnd1h()
    {
        IReadOnlyList<PausePreset> presets = TrayPresentation.TimedPausePresets();
        Assert.Equal(new[] { PausePreset.FifteenMinutes, PausePreset.OneHour }, presets);
    }

    [Theory]
    [InlineData(PausePreset.FifteenMinutes, "Pause for 15 min")]
    [InlineData(PausePreset.OneHour, "Pause for 1 h")]
    public void PausePresetItemText_MatchesMenuLabels(PausePreset preset, string expected)
    {
        Assert.Equal(expected, TrayPresentation.PausePresetItemText(preset));
    }

    [Theory]
    [InlineData(SessionState.Idle, true)]
    [InlineData(SessionState.Detecting, true)]
    [InlineData(SessionState.Active, true)]
    [InlineData(SessionState.Paused, false)]
    public void CanPauseFor_FalseOnlyWhilePaused(SessionState state, bool expected)
    {
        Assert.Equal(expected, TrayPresentation.CanPauseFor(state));
    }

    [Fact]
    public void ApplyItemText_WithoutHotkey_HasNoHint()
    {
        Assert.Equal("Apply projector mode now", TrayPresentation.ApplyItemText(null));
        Assert.Equal("Apply projector mode now", TrayPresentation.ApplyItemText("  "));
    }

    [Fact]
    public void ApplyItemText_WithHotkey_AppendsShortcutHint()
    {
        Assert.Equal("Apply projector mode now  (Win+Alt+P)", TrayPresentation.ApplyItemText("Win+Alt+P"));
    }

    // ── C5: externalized string table ────────────────────────────────────────────

    [Fact]
    public void TrayStrings_All_AreNonEmptyAndDistinct()
    {
        List<string> all = TrayStrings.All().ToList();

        Assert.NotEmpty(all);
        Assert.All(all, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    // ── C1: SettingsViewModel ────────────────────────────────────────────────────

    [Fact]
    public void SettingsViewModel_LoadsDefaults()
    {
        using var h = new Harness();
        var vm = new SettingsViewModel(h.Manager);

        Assert.True(vm.IsEnabled);
        Assert.True(vm.DpiTargetAuto);
        Assert.Equal(100, vm.DpiTargetPercent);
        Assert.True(vm.PreferNativeMode);
        Assert.True(vm.RestoreOnDisconnect);
        Assert.Equal(PausePreset.Indefinite, vm.PausePreset);
        Assert.Equal(2, vm.PausePresetIndex);
        Assert.Equal("Collapsed", vm.SafeModeVisibility);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 60)]
    public void SettingsViewModel_PausePresetIndex_PersistsMinutes(int index, int expectedMinutes)
    {
        using var h = new Harness();
        var vm = new SettingsViewModel(h.Manager);

        vm.PausePresetIndex = index;

        Assert.Equal(expectedMinutes, h.Manager.TimedPauseMinutes);
        Assert.Equal(expectedMinutes, h.Settings.Current.TimedPauseMinutes);
    }

    [Fact]
    public void SettingsViewModel_PausePresetIndex_IndefiniteIsNull()
    {
        using var h = new Harness();
        var vm = new SettingsViewModel(h.Manager);

        vm.PausePresetIndex = 1;
        vm.PausePresetIndex = 2;

        Assert.Null(h.Manager.TimedPauseMinutes);
        Assert.Null(h.Settings.Current.TimedPauseMinutes);
    }

    [Fact]
    public void SettingsViewModel_DpiTargetPercent_PersistsAndClearsAuto()
    {
        using var h = new Harness();
        var vm = new SettingsViewModel(h.Manager);

        vm.DpiTargetPercent = 150;

        Assert.Equal(150, h.Manager.DpiTargetPercent);
        Assert.Equal(150, h.Settings.Current.DpiTargetPercent);
        Assert.False(vm.DpiTargetAuto);

        vm.DpiTargetAuto = true;

        Assert.Equal(0, h.Manager.DpiTargetPercent);
        Assert.True(vm.DpiTargetAuto);
    }

    [Fact]
    public void SettingsViewModel_ResetToDefaults_RestoresDefaults()
    {
        using var h = new Harness();
        var vm = new SettingsViewModel(h.Manager);
        vm.DpiTargetPercent = 250;
        vm.PreferNativeMode = false;
        vm.RestoreOnDisconnect = false;
        vm.PausePresetIndex = 0;

        vm.ResetToDefaults();

        Assert.Equal(0, h.Manager.DpiTargetPercent);
        Assert.True(h.Manager.PreferNativeMode);
        Assert.True(h.Manager.RestoreOnDisconnect);
        Assert.Null(h.Manager.TimedPauseMinutes);
        Assert.True(h.Manager.IsEnabled);
    }

    [Fact]
    public void SettingsViewModel_ExportSettings_WritesJson()
    {
        using var h = new Harness();
        var vm = new SettingsViewModel(h.Manager);
        vm.DpiTargetPercent = 175;
        string path = Path.Combine(Path.GetTempPath(), "beam-settings-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            vm.ExportSettings(path);

            string json = File.ReadAllText(path);
            // STJ default options: PascalCase property names, compact separators (no space).
            Assert.Contains("\"DpiTargetPercent\":175", json);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // ── C4: DiagnosticsViewModel ─────────────────────────────────────────────────

    private const string User = "patbaum";
    private const string Machine = "PAT-PC-01";

    private static TimelineBuffer TimelineWithSecret()
    {
        var buffer = new TimelineBuffer();
        buffer.Add(new TimelineEvent(
            DateTimeOffset.UtcNow, LogLevel.Warn, "Monitor",
            $"display changed on {Machine} for {User}",
            Array.Empty<KeyValuePair<string, object?>>()));
        return buffer;
    }

    private static DiagnosticsViewModel MakeVm(Harness h, TimelineBuffer timeline, string logDir) =>
        new(h.Manager, h.Session, timeline, "0.2.0", logDir, null, User, Machine);

    [Fact]
    public void DiagnosticsViewModel_EventLines_ReflectBuffer()
    {
        using var h = new Harness();
        var vm = MakeVm(h, TimelineWithSecret(), Path.GetTempPath());

        Assert.Equal(1, vm.EventCount);
        Assert.Equal(TimelineBuffer.DefaultCapacity, vm.EventCapacity);
        string line = Assert.Single(vm.EventLines());
        Assert.Contains("display changed", line);
    }

    [Fact]
    public void DiagnosticsViewModel_Summary_IsSanitized()
    {
        using var h = new Harness();
        var vm = MakeVm(h, TimelineWithSecret(), Path.GetTempPath());

        string summary = vm.BuildSummary();

        Assert.Contains("Beam 0.2.0", summary);
        Assert.Contains("Session state: Idle", summary);
        Assert.True(LogSanitizer.IsClean(summary, User, Machine), summary);
        Assert.Contains(LogSanitizer.Redacted, summary);
    }

    [Fact]
    public void DiagnosticsViewModel_ExportBundle_WritesSanitizedZip()
    {
        using var h = new Harness();
        string dir = Path.Combine(Path.GetTempPath(), "beam-vm-diag-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = MakeVm(h, TimelineWithSecret(), dir);

            string zipPath = vm.ExportBundle(dir);

            Assert.True(File.Exists(zipPath));
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = Assert.Single(zip.Entries.Where(e => e.Name == "summary.txt").ToList());
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            string text = reader.ReadToEnd();
            Assert.True(LogSanitizer.IsClean(text, User, Machine), text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DiagnosticsViewModel_ReevaluateAndResetSafeMode_DoNotThrow()
    {
        using var h = new Harness();
        var vm = MakeVm(h, new TimelineBuffer(), Path.GetTempPath());

        await vm.ReevaluateNow();
        vm.ResetSafeMode();

        Assert.False(vm.IsSafeMode);
        Assert.Equal(SessionState.Idle, vm.SessionState);
    }
}

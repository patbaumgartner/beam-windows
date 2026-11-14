using Beam.Core.Settings;

namespace Beam.App.Tests;

/// <summary>
/// B4: PresentationManager loads persisted settings through <see cref="ISettingsStore"/>
/// and saves them on every change, so settings survive a restart.
/// </summary>
public class PersistedSettingsTests
{
    [Fact]
    public async Task SetEnabled_Persists_AndSurvivesRestart()
    {
        var store = new FakeSettingsStore();
        using (var h = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store))
        {
            Assert.True(h.Manager.IsEnabled); // default before any change
            await h.Manager.SetEnabledAsync(false);
            Assert.True(store.SaveCalls >= 1);
            Assert.False(store.Current.IsEnabled);
        }

        // "Restart": a new manager over the same store loads the persisted value.
        using var h2 = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store);
        Assert.True(store.LoadCalls >= 1);
        Assert.False(h2.Manager.IsEnabled);
    }

    [Fact]
    public void PolicySetters_Persist()
    {
        var store = new FakeSettingsStore();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store);

        h.Manager.UseMode = false;
        h.Manager.UseDpi = false;
        h.Manager.UseNightLight = false;

        Assert.False(store.Current.UseMode);
        Assert.False(store.Current.UseDpi);
        Assert.False(store.Current.UseNightLight);
    }

    [Fact]
    public void IgnoreDisplay_Persists_AndLoadsOnRestart()
    {
        var store = new FakeSettingsStore();
        using (var h = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store))
        {
            h.Manager.IgnoreDisplay(@"monitor\DELL");
            Assert.Contains(@"monitor\DELL", store.Current.IgnoredDisplays);
        }

        using var h2 = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store);
        Assert.Contains(@"monitor\DELL", h2.Manager.IgnoredDisplays);
    }

    [Fact]
    public void UnignoreDisplay_Persists()
    {
        var store = new FakeSettingsStore();
        store.Save(BeamSettings.Defaults with { IgnoredDisplays = new[] { @"monitor\DELL" } });

        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store);
        Assert.Contains(@"monitor\DELL", h.Manager.IgnoredDisplays);

        h.Manager.UnignoreDisplay(@"monitor\DELL");

        Assert.Empty(store.Current.IgnoredDisplays);
    }

    [Fact]
    public void ExtrasAndHotkey_Persist()
    {
        var store = new FakeSettingsStore();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store);

        h.Manager.KeepAwake = false;
        h.Manager.DoNotDisturb = false;
        h.Manager.AudioToHdmi = false;
        h.Manager.Hotkey = "Win+Alt+O";

        Assert.False(store.Current.KeepAwake);
        Assert.False(store.Current.DoNotDisturb);
        Assert.False(store.Current.AudioToHdmi);
        Assert.Equal("Win+Alt+O", store.Current.Hotkey);
    }

    [Fact]
    public void ExtrasAndHotkey_LoadFromStore()
    {
        var store = new FakeSettingsStore();
        store.Save(BeamSettings.Defaults with
        {
            KeepAwake = false,
            DoNotDisturb = false,
            AudioToHdmi = false,
            Hotkey = "Ctrl+Shift+P",
        });

        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store);

        Assert.False(h.Manager.KeepAwake);
        Assert.False(h.Manager.DoNotDisturb);
        Assert.False(h.Manager.AudioToHdmi);
        Assert.Equal("Ctrl+Shift+P", h.Manager.Hotkey);
    }

    [Fact]
    public void Hotkey_EmptyFallsBackToDefault()
    {
        var store = new FakeSettingsStore();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store);

        h.Manager.Hotkey = "  ";

        Assert.Equal(BeamSettings.Defaults.Hotkey, h.Manager.Hotkey);
        Assert.Equal(BeamSettings.Defaults.Hotkey, store.Current.Hotkey);
    }

    [Fact]
    public void NoStore_ChangesDoNotThrow()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch());

        h.Manager.UseMode = false;
        h.Manager.IgnoreDisplay(@"monitor\DELL");

        Assert.False(h.Manager.UseMode);
        Assert.Contains(@"monitor\DELL", h.Manager.IgnoredDisplays);
    }

    [Fact]
    public void CurrentSettings_ReflectsLiveValues()
    {
        var store = new FakeSettingsStore();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), settings: store);

        h.Manager.IgnoreDisplay(@"monitor\DELL");

        BeamSettings snapshot = h.Manager.CurrentSettings;
        Assert.Contains(@"monitor\DELL", snapshot.IgnoredDisplays);
        Assert.Equal(BeamSettings.Defaults.Hotkey, snapshot.Hotkey);
        Assert.True(snapshot.IsEnabled);
    }
}

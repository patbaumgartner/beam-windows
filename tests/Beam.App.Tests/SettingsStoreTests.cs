using System.Text.Json;
using Beam.App.Settings;
using Beam.Core.Settings;

namespace Beam.App.Tests;

/// <summary>
/// B4: the on-disk settings store — round-trip, atomic write, corruption handling,
/// forward compatibility (unknown keys preserved, newer versions not clobbered).
/// Each test gets its own temp directory.
/// </summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "beam-settings-tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private SettingsStore NewStore() => new(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaultsInstance()
    {
        BeamSettings s = NewStore().Load();

        Assert.Same(BeamSettings.Defaults, s);
        Assert.Equal(BeamSettings.CurrentVersion, s.Version);
        Assert.True(s.IsEnabled);
        Assert.True(s.KeepAwake);
        Assert.Equal("Win+Alt+P", s.Hotkey);
        Assert.Empty(s.IgnoredDisplays);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var settings = BeamSettings.Defaults with
        {
            IsEnabled = false,
            UseDpi = false,
            KeepAwake = false,
            DoNotDisturb = false,
            IgnoredDisplays = new[] { @"monitor\DELL", @"monitor\EPS" },
            Hotkey = "Win+Alt+O",
        };

        NewStore().Save(settings);
        BeamSettings loaded = NewStore().Load();

        Assert.Equal(settings.Version, loaded.Version);
        Assert.Equal(settings.IsEnabled, loaded.IsEnabled);
        Assert.Equal(settings.UseMode, loaded.UseMode);
        Assert.Equal(settings.UseDpi, loaded.UseDpi);
        Assert.Equal(settings.UseNightLight, loaded.UseNightLight);
        Assert.Equal(settings.KeepAwake, loaded.KeepAwake);
        Assert.Equal(settings.DoNotDisturb, loaded.DoNotDisturb);
        Assert.Equal(settings.AudioToHdmi, loaded.AudioToHdmi);
        Assert.Equal(settings.IgnoredDisplays, loaded.IgnoredDisplays); // xunit compares sequences structurally
        Assert.Equal(settings.Hotkey, loaded.Hotkey);
    }

    [Fact]
    public void Save_IsAtomic_TmpFileGone()
    {
        NewStore().Save(BeamSettings.Defaults with { UseMode = false });

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_SetAside_RaisesEventOnce_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var store = NewStore();
        string? badPath = null;
        int events = 0;
        store.CorruptFileSetAside += p => { events++; badPath = p; };

        Assert.Same(BeamSettings.Defaults, store.Load());

        Assert.Equal(1, events);
        Assert.NotNull(badPath);
        Assert.EndsWith(".bad", badPath, StringComparison.Ordinal);
        Assert.True(File.Exists(badPath));
        Assert.False(File.Exists(SettingsPath)); // moved, not copied

        // Second load: the file is gone — defaults again, no new event, no new .bad file.
        Assert.Same(BeamSettings.Defaults, store.Load());
        Assert.Equal(1, events);
    }

    [Fact]
    public void Load_OversizedFile_SetAside_NotLoaded()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, new string('x', (int)SettingsStore.MaxReadBytes + 1));

        var store = NewStore();
        string? badPath = null;
        store.CorruptFileSetAside += p => badPath = p;

        Assert.Same(BeamSettings.Defaults, store.Load());
        Assert.NotNull(badPath);
        Assert.True(File.Exists(badPath));
    }

    [Fact]
    public void Load_WrongTypedKey_TreatedAsCorrupt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "Version": 1, "IsEnabled": "yes please" }""");

        var store = NewStore();
        string? badPath = null;
        store.CorruptFileSetAside += p => badPath = p;

        Assert.Same(BeamSettings.Defaults, store.Load());
        Assert.NotNull(badPath);
    }

    [Fact]
    public void Save_PreservesUnknownKeys()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """
            {
              "Version": 1,
              "IsEnabled": true,
              "FutureKeyFromNewerBuild": { "nested": [1, 2, 3] }
            }
            """);

        var store = NewStore();
        store.Load();
        store.Save(BeamSettings.Defaults with { DoNotDisturb = false });

        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(3, doc.RootElement.GetProperty("FutureKeyFromNewerBuild").GetProperty("nested").GetArrayLength());
        Assert.False(doc.RootElement.GetProperty("DoNotDisturb").GetBoolean());
        Assert.Equal(BeamSettings.CurrentVersion, doc.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void Load_NewerVersion_ReturnsDefaults_WithoutClobbering()
    {
        Directory.CreateDirectory(_dir);
        const string original = """
            {
              "Version": 99,
              "IsEnabled": false,
              "FutureKey": 42
            }
            """;
        File.WriteAllText(SettingsPath, original);

        var store = NewStore();
        string? badPath = null;
        store.CorruptFileSetAside += p => badPath = p;

        // Newer schema: defaults are used, but the file is NOT treated as corrupt...
        Assert.Same(BeamSettings.Defaults, store.Load());
        Assert.Null(badPath);
        Assert.Equal(original, File.ReadAllText(SettingsPath)); // ...and left untouched

        // A later save from this older build keeps the newer build's keys, writes its own version.
        store.Save(BeamSettings.Defaults with { UseMode = false });
        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(BeamSettings.CurrentVersion, doc.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal(42, doc.RootElement.GetProperty("FutureKey").GetInt32());
        Assert.False(doc.RootElement.GetProperty("UseMode").GetBoolean());
    }
}

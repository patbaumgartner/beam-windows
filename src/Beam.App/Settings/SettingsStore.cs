using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beam.Core.Diagnostics;
using Beam.Core.Settings;

namespace Beam.App.Settings;

/// <summary>
/// B4 persisted user settings at <c>%LOCALAPPDATA%\Beam\settings.json</c>.
///
/// <para><b>Atomic writes:</b> every save goes to <c>settings.json.tmp</c> and is then
/// renamed over the real file, so a crash can never leave a half-written settings file.</para>
///
/// <para><b>Forward compatibility:</b> unknown keys already in the file are preserved on
/// save (a newer Beam's keys survive an older build writing the file). A file whose
/// <c>Version</c> is newer than this build is left untouched and defaults are used.</para>
///
/// <para><b>Corruption:</b> an unreadable or oversized file is renamed to
/// <c>settings.json.bad</c> (timestamped when that exists), <see cref="CorruptFileSetAside"/>
/// fires once, and defaults are used — the app always starts.</para>
///
/// <para>Keys are PascalCase, matching the journal's JSON and STJ's default naming.</para>
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly string _tmpPath;

    /// <summary>Serializes <see cref="Save"/>: every save funnels through the same tmp file,
    /// so two concurrent saves (display-change handler + startup + tray) would race on
    /// <c>settings.json.tmp</c> and one save would be lost with "file in use by another
    /// process" (observed in the 2026-11-16 lecture log).</summary>
    private readonly object _saveLock = new();

    /// <summary>The last parsed root object, kept so <see cref="Save"/> preserves unknown keys.</summary>
    private JsonObject? _raw;

    /// <summary>
    /// Maximum settings file size accepted by <see cref="Load"/>. Settings are a few hundred
    /// bytes; anything larger is corruption or an attack — set it aside instead of loading it.
    /// </summary>
    public const long MaxReadBytes = 256 * 1024;

    public SettingsStore(string? dir = null)
    {
        dir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Beam");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        _tmpPath = _path + ".tmp";
    }

    /// <inheritdoc/>
    public event Action<string>? CorruptFileSetAside;

    /// <summary>Loads the settings; never throws. See class remarks for the file-handling rules.</summary>
    public BeamSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _raw = null;
                return BeamSettings.Defaults;
            }

            long length = new FileInfo(_path).Length;
            if (length > MaxReadBytes)
            {
                // A4: log the file name only — full paths carry the user profile name.
                Log.Warn("Settings file too large — set aside, using defaults", "Settings",
                    ("file", Path.GetFileName(_path)),
                    ("bytes", length),
                    ("maxBytes", MaxReadBytes));
                SetAsideBad();
                _raw = null;
                return BeamSettings.Defaults;
            }

            string json = File.ReadAllText(_path);
            if (JsonNode.Parse(json) is not JsonObject obj)
            {
                SetAsideBad();
                _raw = null;
                return BeamSettings.Defaults;
            }

            int version = obj[nameof(BeamSettings.Version)] is { } v && v.GetValueKind() == JsonValueKind.Number
                ? v.GetValue<int>()
                : BeamSettings.CurrentVersion;
            if (version > BeamSettings.CurrentVersion)
            {
                // Written by a newer Beam: do not guess the schema, do not clobber the file.
                Log.Warn("Settings written by a newer Beam version — using defaults, file left untouched", "Settings",
                    ("file", Path.GetFileName(_path)),
                    ("version", version),
                    ("currentVersion", BeamSettings.CurrentVersion));
                _raw = obj; // keep, so a later Save still preserves the newer build's keys
                return BeamSettings.Defaults;
            }

            BeamSettings settings = new(
                Version: version,
                IsEnabled: GetBool(obj, nameof(BeamSettings.IsEnabled), BeamSettings.Defaults.IsEnabled),
                UseMode: GetBool(obj, nameof(BeamSettings.UseMode), BeamSettings.Defaults.UseMode),
                UseDpi: GetBool(obj, nameof(BeamSettings.UseDpi), BeamSettings.Defaults.UseDpi),
                UseNightLight: GetBool(obj, nameof(BeamSettings.UseNightLight), BeamSettings.Defaults.UseNightLight),
                KeepAwake: GetBool(obj, nameof(BeamSettings.KeepAwake), BeamSettings.Defaults.KeepAwake),
                DoNotDisturb: GetBool(obj, nameof(BeamSettings.DoNotDisturb), BeamSettings.Defaults.DoNotDisturb),
                AudioToHdmi: GetBool(obj, nameof(BeamSettings.AudioToHdmi), BeamSettings.Defaults.AudioToHdmi),
                IgnoredDisplays: GetStringList(obj, nameof(BeamSettings.IgnoredDisplays)),
                Hotkey: GetString(obj, nameof(BeamSettings.Hotkey), BeamSettings.Defaults.Hotkey),
                ProjectorTopology: GetString(obj, nameof(BeamSettings.ProjectorTopology), BeamSettings.Defaults.ProjectorTopology));

            _raw = obj;
            return settings;
        }
        catch (Exception ex)
        {
            // A corrupt settings file must never prevent the app from starting.
            Log.Warn("Settings file unreadable — set aside, using defaults", "Settings",
                ("file", Path.GetFileName(_path)),
                ("error", ex.Message));
            SetAsideBad();
            _raw = null;
            return BeamSettings.Defaults;
        }
    }

    /// <summary>
    /// Atomically persists the settings (tmp + rename), preserving unknown keys already
    /// present in the file. Never throws.
    /// </summary>
    public void Save(BeamSettings settings)
    {
        lock (_saveLock)
        {
            try
            {
                JsonObject obj = _raw is not null ? (JsonObject)_raw.DeepClone() : new JsonObject();
                obj[nameof(BeamSettings.Version)] = BeamSettings.CurrentVersion;
                obj[nameof(BeamSettings.IsEnabled)] = settings.IsEnabled;
                obj[nameof(BeamSettings.UseMode)] = settings.UseMode;
                obj[nameof(BeamSettings.UseDpi)] = settings.UseDpi;
                obj[nameof(BeamSettings.UseNightLight)] = settings.UseNightLight;
                obj[nameof(BeamSettings.KeepAwake)] = settings.KeepAwake;
                obj[nameof(BeamSettings.DoNotDisturb)] = settings.DoNotDisturb;
                obj[nameof(BeamSettings.AudioToHdmi)] = settings.AudioToHdmi;
                obj[nameof(BeamSettings.IgnoredDisplays)] = new JsonArray(
                    settings.IgnoredDisplays.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());
                obj[nameof(BeamSettings.Hotkey)] = settings.Hotkey;
                obj[nameof(BeamSettings.ProjectorTopology)] = settings.ProjectorTopology;

                string json = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_tmpPath, json);
                // Atomic on NTFS: a single rename replaces the old file (same pattern as the journal).
                File.Move(_tmpPath, _path, overwrite: true);
                _raw = obj;
            }
            catch (Exception ex)
            {
                // A settings write failure must never take the app down.
                Log.Warn("Settings save failed — continuing with in-memory settings", "Settings",
                    ("file", Path.GetFileName(_path)),
                    ("error", ex.Message));
            }
        }
    }

    private static bool GetBool(JsonObject obj, string key, bool fallback)
    {
        JsonNode? node = obj[key];
        return node is null ? fallback : node.GetValue<bool>();
    }

    private static string GetString(JsonObject obj, string key, string fallback)
    {
        JsonNode? node = obj[key];
        return node is null ? fallback : node.GetValue<string>();
    }

    private static IReadOnlyList<string> GetStringList(JsonObject obj, string key)
    {
        if (obj[key] is not JsonArray arr)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>(arr.Count);
        foreach (JsonNode? item in arr)
        {
            if (item is JsonValue value
                && value.TryGetValue<string>(out string? s)
                && !string.IsNullOrEmpty(s))
            {
                list.Add(s);
            }
        }

        return list;
    }

    /// <summary>Renames the corrupt file to <c>*.bad</c> and raises <see cref="CorruptFileSetAside"/> once.</summary>
    private void SetAsideBad()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            string bad = _path + ".bad";
            if (File.Exists(bad))
            {
                bad = _path + "." + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bad";
            }

            File.Move(_path, bad);
            Log.Warn("Corrupt settings file set aside — defaults in use", "Settings",
                ("badFile", Path.GetFileName(bad)));
            CorruptFileSetAside?.Invoke(bad);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not set aside the corrupt settings file", "Settings",
                ("error", ex.Message));
        }
    }
}

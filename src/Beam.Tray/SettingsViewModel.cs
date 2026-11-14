using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Beam.App.Presentation;
using Beam.Core.Settings;

namespace Beam.Tray;

/// <summary>
/// C1: the settings window's view model. Pure logic over <see cref="PresentationManager"/>
/// — no WPF types — so it is fully unit-testable in Beam.Tray.Tests. Every setter applies
/// immediately; the manager persists (debounced through its own PersistSettings).
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PresentationManager _pm;

    /// <summary>Opens the log folder; injected by the composition root (host concern).</summary>
    public Action? OpenLogFolder { get; set; }

    public SettingsViewModel(PresentationManager pm)
    {
        _pm = pm ?? throw new ArgumentNullException(nameof(pm));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── General ──────────────────────────────────────────────────────────────────

    public bool IsEnabled
    {
        get => _pm.IsEnabled;
        set { _ = _pm.SetEnabledAsync(value); Raise(); }
    }

    public string Hotkey
    {
        get => _pm.Hotkey;
        set { _pm.Hotkey = value; Raise(); }
    }

    /// <summary>C1/C2: the preferred pause duration, as a preset.</summary>
    public PausePreset PausePreset
    {
        get => PausePresetExtensions.FromMinutes(_pm.TimedPauseMinutes);
        set { _pm.TimedPauseMinutes = value.ToMinutes(); Raise(); Raise(nameof(PausePresetIndex)); }
    }

    /// <summary>XAML binding helper: combo index for <see cref="PausePreset"/> (0=15 min, 1=1 h, 2=indefinite).</summary>
    public int PausePresetIndex
    {
        get => PausePreset switch
        {
            PausePreset.FifteenMinutes => 0,
            PausePreset.OneHour => 1,
            _ => 2,
        };
        set
        {
            PausePreset = value switch
            {
                0 => PausePreset.FifteenMinutes,
                1 => PausePreset.OneHour,
                _ => PausePreset.Indefinite,
            };
        }
    }

    // ── Projector mode ───────────────────────────────────────────────────────────

    public bool UseMode { get => _pm.UseMode; set { _pm.UseMode = value; Raise(); } }
    public bool UseDpi { get => _pm.UseDpi; set { _pm.UseDpi = value; Raise(); } }
    public bool UseNightLight { get => _pm.UseNightLight; set { _pm.UseNightLight = value; Raise(); } }
    public bool RestoreOnDisconnect { get => _pm.RestoreOnDisconnect; set { _pm.RestoreOnDisconnect = value; Raise(); } }
    public bool PreferNativeMode { get => _pm.PreferNativeMode; set { _pm.PreferNativeMode = value; Raise(); } }

    /// <summary>C1: true when the DPI target is automatic (planner decides).</summary>
    public bool DpiTargetAuto
    {
        get => _pm.DpiTargetPercent == 0;
        set { if (value) { _pm.DpiTargetPercent = 0; Raise(nameof(DpiTargetAuto)); Raise(nameof(DpiTargetPercent)); } }
    }

    /// <summary>C1: fixed DPI target in percent (only meaningful when <see cref="DpiTargetAuto"/> is false).</summary>
    public int DpiTargetPercent
    {
        get => _pm.DpiTargetPercent == 0 ? 100 : _pm.DpiTargetPercent;
        set { _pm.DpiTargetPercent = value; Raise(nameof(DpiTargetPercent)); Raise(nameof(DpiTargetAuto)); }
    }

    // ── Extras (B3) ──────────────────────────────────────────────────────────────

    public bool KeepAwake { get => _pm.KeepAwake; set { _pm.KeepAwake = value; Raise(); } }
    public bool DoNotDisturb { get => _pm.DoNotDisturb; set { _pm.DoNotDisturb = value; Raise(); } }
    public bool AudioToHdmi { get => _pm.AudioToHdmi; set { _pm.AudioToHdmi = value; Raise(); } }

    // ── Devices (C1) ─────────────────────────────────────────────────────────────

    /// <summary>C1: the known-devices table (name + identity + auto-apply).</summary>
    public IReadOnlyList<DeviceProfile> Devices => _pm.KnownDevices;

    /// <summary>C1: per-device auto-apply toggle (false = never triggers the fix).</summary>
    public void SetDeviceAutoApply(string identity, bool autoApply)
    {
        _pm.SetDeviceAutoApply(identity, autoApply);
        Raise(nameof(Devices));
    }

    /// <summary>C1: forget a device (removes it from the known list and any ignore).</summary>
    public void ForgetDevice(string identity)
    {
        _pm.ForgetDevice(identity);
        Raise(nameof(Devices));
    }

    /// <summary>B5: ignore/un-ignore a display identity.</summary>
    public void SetIgnored(string identity, bool ignored)
    {
        if (ignored)
        {
            _pm.IgnoreDisplay(identity);
        }
        else
        {
            _pm.UnignoreDisplay(identity);
        }
    }

    public IReadOnlyCollection<string> IgnoredDisplays => _pm.IgnoredDisplays;

    // ── Advanced ─────────────────────────────────────────────────────────────────

    public bool IsSafeMode => _pm.IsSafeMode;

    /// <summary>XAML binding helper: Visibility for the safe-mode warning line.</summary>
    public string SafeModeVisibility => _pm.IsSafeMode ? "Visible" : "Collapsed";

    /// <summary>C4: re-run the trigger check right now ("why didn't it trigger?").</summary>
    public Task ReevaluateNow() => _pm.CheckCurrentStateAsync();

    /// <summary>C4: clear safe mode so automatic fixes resume.</summary>
    public void ResetSafeMode()
    {
        _pm.ClearSafeMode();
        Raise(nameof(IsSafeMode));
    }

    /// <summary>C1: export the current settings JSON to <paramref name="path"/> (A4: sanitized on read-back by the store).</summary>
    public string ExportSettings(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string json = JsonSerializer.Serialize(_pm.CurrentSettings);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>C1: reset every setting to <see cref="BeamSettings.Defaults"/> (except the first-run toast flag).</summary>
    public void ResetToDefaults()
    {
        BeamSettings d = BeamSettings.Defaults;
        _ = _pm.SetEnabledAsync(d.IsEnabled);
        _pm.UseMode = d.UseMode;
        _pm.UseDpi = d.UseDpi;
        _pm.UseNightLight = d.UseNightLight;
        _pm.KeepAwake = d.KeepAwake;
        _pm.DoNotDisturb = d.DoNotDisturb;
        _pm.AudioToHdmi = d.AudioToHdmi;
        _pm.Hotkey = d.Hotkey;
        _pm.TimedPauseMinutes = d.TimedPauseMinutes;
        _pm.RestoreOnDisconnect = d.RestoreOnDisconnect;
        _pm.DpiTargetPercent = d.DpiTargetPercent;
        _pm.PreferNativeMode = d.PreferNativeMode;
        foreach (string identity in _pm.IgnoredDisplays.ToList())
        {
            _pm.UnignoreDisplay(identity);
        }

        Raise(nameof(IsEnabled));
        Raise(nameof(UseMode));
        Raise(nameof(UseDpi));
        Raise(nameof(UseNightLight));
        Raise(nameof(KeepAwake));
        Raise(nameof(DoNotDisturb));
        Raise(nameof(AudioToHdmi));
        Raise(nameof(Hotkey));
        Raise(nameof(PausePreset));
        Raise(nameof(RestoreOnDisconnect));
        Raise(nameof(DpiTargetPercent));
        Raise(nameof(DpiTargetAuto));
        Raise(nameof(PreferNativeMode));
        Raise(nameof(IgnoredDisplays));
    }
}

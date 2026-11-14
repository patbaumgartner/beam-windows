using Beam.Core.Ports;
using Microsoft.Win32;

namespace Beam.Windows.Extras;

/// <summary>
/// Reads and writes the per-user "toast notifications enabled" value that backs the
/// Windows Focus Assist / Do-Not-Disturb toggle, so presenting silences popups.
/// Best-effort: reads never throw, and writes are verified by re-reading.
/// </summary>
public sealed class DndController : IDndController
{
    private const string SettingsKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    /// <summary>The global toasts value name (DWORD: 1 = enabled, 0 = disabled).</summary>
    public const string ToastsValueName = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

    /// <inheritdoc />
    public bool? SnapshotToastsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
            return key?.GetValue(ToastsValueName) switch
            {
                int i => i != 0,
                uint u => u != 0,
                long l => l != 0,
                _ => null,
            };
        }
        catch
        {
            // Unreadable setting = unknown; the caller treats null as "do not touch".
            return null;
        }
    }

    /// <inheritdoc />
    public bool SetToastsEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key.SetValue(ToastsValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            return SnapshotToastsEnabled() == enabled; // verify the write by re-reading
        }
        catch
        {
            return false;
        }
    }
}

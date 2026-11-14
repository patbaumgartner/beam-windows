namespace Beam.Tray;

/// <summary>
/// C5: all user-facing strings of the new UI (settings window, diagnostics window,
/// tray menu additions, first-run toast) live here in one place, so the app is
/// localizable later without touching layout or logic. Pure data — no WinForms/WPF
/// types, so the strings are unit-testable.
/// </summary>
internal static class TrayStrings
{
    // Tray menu (C2 additions).
    public const string SettingsMenuItem = "Settings…";
    public const string DiagnosticsMenuItem = "Diagnostics…";
    public const string PauseForPrefix = "Pause for ";

    /// <summary>C2: shortcut hint shown on the "Apply projector mode now" item.</summary>
    public static string ApplyShortcutHint(string hotkeyDisplay) => $"({hotkeyDisplay})";

    // C3: first-run toast (shown once per install).
    public const string FirstRunToastTitle = "Beam";
    public const string FirstRunToastText =
        "Beam is running. Plug in a projector — I'll handle the rest.";

    // Settings window (C1).
    public const string SettingsWindowTitle = "Beam settings";
    public const string GeneralHeader = "General";
    public const string ProjectorModeHeader = "Projector mode";
    public const string ExtrasHeader = "Extras";
    public const string DevicesHeader = "Devices";
    public const string AdvancedHeader = "Advanced";
    public const string DpiAutoText = "Automatic";
    public const string DpiPercentSuffix = "%";
    public const string ResetDefaultsButton = "Reset to defaults";
    public const string ExportSettingsButton = "Export settings…";
    public const string OpenLogFolderButton = "Open log folder";
    public const string SafeModeActiveText = "Safe mode is active — automatic fixes are paused.";
    public const string ResetSafeModeButton = "Reset safe mode";
    public const string ReevaluateNowButton = "Re-evaluate now";
    public const string CloseButton = "Close";

    // Diagnostics window (C4).
    public const string DiagnosticsWindowTitle = "Beam diagnostics";
    public const string CopySummaryButton = "Copy summary";
    public const string ExportBundleButton = "Export bundle…";
    public const string SummaryCopiedToast = "Summary copied to the clipboard.";
    public const string BundleExportedToast = "Diagnostics bundle exported to your Desktop.";
    public const string BundleFailedToast = "Could not export the diagnostics bundle.";

    /// <summary>Non-empty check used by the string table test (C5).</summary>
    public static IEnumerable<string> All() => new[]
    {
        SettingsMenuItem, DiagnosticsMenuItem, PauseForPrefix,
        FirstRunToastTitle, FirstRunToastText,
        SettingsWindowTitle, GeneralHeader, ProjectorModeHeader, ExtrasHeader,
        DevicesHeader, AdvancedHeader, DpiAutoText, DpiPercentSuffix,
        ResetDefaultsButton, ExportSettingsButton, OpenLogFolderButton,
        SafeModeActiveText, ResetSafeModeButton, ReevaluateNowButton, CloseButton,
        DiagnosticsWindowTitle, CopySummaryButton, ExportBundleButton,
        SummaryCopiedToast, BundleExportedToast, BundleFailedToast,
    };
}

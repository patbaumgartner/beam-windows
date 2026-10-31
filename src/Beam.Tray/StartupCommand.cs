namespace Beam.Tray;

/// <summary>
/// Builds and matches the HKCU <c>Run</c> value that starts Beam at sign-in.
///
/// The installer's "Launch Beam at sign-in" task and the tray menu both write to the
/// same key, and both should produce an identical value so each can see the other's
/// state. Matching is tolerant of the trailing <c>--hidden</c> that suppresses the
/// startup balloon once sign-in launches the app.
/// </summary>
public static class StartupCommand
{
    /// <summary>The per-user startup key (no hive prefix — always HKCU).</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The value name under <see cref="RunKeyPath"/>.</summary>
    public const string ValueName = "Beam";

    /// <summary>
    /// True when a stored <c>Run</c> value starts the given executable, ignoring a
    /// trailing <c>--hidden</c> argument. Both the bare and hidden forms are accepted so
    /// the tray menu and the installer agree on what "enabled" means.
    /// </summary>
    public static bool IsAutoStartValue(string exePath, string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(storedValue))
        {
            return false;
        }

        string bare = $"\"{exePath}\"";
        return storedValue.Equals(bare, StringComparison.OrdinalIgnoreCase)
            || storedValue.Equals($"{bare} --hidden", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the value written to the <c>Run</c> key.
    /// </summary>
    /// <param name="exePath">Full path to Beam.exe.</param>
    /// <param name="launchHidden">
    /// When true, appends <c>--hidden</c> so the app starts without a status balloon.
    /// Matches the installer task and is the recommended form for sign-in launches.
    /// </param>
    public static string ValueFor(string exePath, bool launchHidden = true)
    {
        string bare = $"\"{exePath}\"";
        return launchHidden ? $"{bare} --hidden" : bare;
    }
}

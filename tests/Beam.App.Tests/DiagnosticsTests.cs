using System.IO.Compression;
using System.Text;
using Beam.App.Diagnostics;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Core.Settings;

namespace Beam.App.Tests;

/// <summary>
/// C4/A4: the diagnostics summary and export bundle must never leak the machine or user
/// name — every line is sanitized with the supplied secrets before it is shown or written.
/// </summary>
public class DiagnosticsTests
{
    private const string User = "patbaum";
    private const string Machine = "PAT-PC-01";

    private static TimelineBuffer TimelineWithSecret()
    {
        var buffer = new TimelineBuffer(10);
        buffer.Add(new TimelineEvent(
            DateTimeOffset.UtcNow, LogLevel.Warn, "Monitor",
            $"display changed on {Machine} for user {User}",
            new[] { new KeyValuePair<string, object?>("path", $@"C:\Users\{User}\AppData") }));
        buffer.Add(new TimelineEvent(
            DateTimeOffset.UtcNow, LogLevel.Info, "Fix",
            "Display mode applied and verified",
            Array.Empty<KeyValuePair<string, object?>>()));
        return buffer;
    }

    [Fact]
    public void Summary_ContainsStateSettingsAndEvents()
    {
        string summary = DiagnosticsSummary.Build(
            "0.2.0", SessionState.Active, isSafeMode: false,
            BeamSettings.Defaults, TimelineWithSecret(), User, Machine);

        Assert.Contains("Beam 0.2.0", summary);
        Assert.Contains("Session state: Active", summary);
        Assert.Contains("enabled=True", summary);
        Assert.Contains("Recent events (2/10", summary);
        Assert.Contains("Display mode applied and verified", summary);
    }

    [Fact]
    public void Summary_SanitizesSecrets()
    {
        string summary = DiagnosticsSummary.Build(
            "0.2.0", SessionState.Active, isSafeMode: true,
            BeamSettings.Defaults, TimelineWithSecret(), User, Machine);

        Assert.True(LogSanitizer.IsClean(summary, User, Machine));
        Assert.Contains(LogSanitizer.Redacted, summary);
        Assert.Contains("SAFE MODE", summary);
    }

    [Fact]
    public void Summary_SafeModeFlagShown()
    {
        string summary = DiagnosticsSummary.Build(
            "0.2.0", SessionState.Paused, isSafeMode: true,
            BeamSettings.Defaults with { TimedPauseMinutes = 15, DpiTargetPercent = 150 },
            new TimelineBuffer(), User, Machine);

        Assert.Contains("Session state: Paused (SAFE MODE)", summary);
        Assert.Contains("pauseMinutes=15", summary);
        Assert.Contains("dpiTarget=150%", summary);
    }

    [Fact]
    public void Exporter_WritesZipWithSanitizedEntries()
    {
        string dir = Path.Combine(Path.GetTempPath(), "beam-diag-test-" + Guid.NewGuid().ToString("N"));
        string logDir = Path.Combine(dir, "logs");
        Directory.CreateDirectory(logDir);
        try
        {
            string logFile = Path.Combine(logDir, "beam.log");
            File.WriteAllText(logFile, $"started on {Machine}\nuser profile C:\\Users\\{User}\\...\nfix ok");
            string statusFile = Path.Combine(logDir, "status.json");
            File.WriteAllText(statusFile, $"{{\"state\":\"Active\",\"note\":\"{User} on {Machine}\"}}");

            var exporter = new DiagnosticsBundleExporter();
            string zipPath = exporter.Export(
                dir, "summary text with " + User, new[] { logFile, Path.Combine(logDir, "missing.log") }, statusFile, User, Machine);

            Assert.True(File.Exists(zipPath));
            Assert.StartsWith("beam-diagnostics-", Path.GetFileName(zipPath));

            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.Name).ToArray();
            Assert.Contains("summary.txt", names);
            Assert.Contains("beam.log", names);
            Assert.Contains("status.json", names);
            Assert.DoesNotContain("missing.log", names); // missing files are skipped, not fatal

            string AllText() => string.Join("\n", zip.Entries.Select(e =>
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            }));

            string all = AllText();
            Assert.True(LogSanitizer.IsClean(all, User, Machine), "bundle leaked a secret: " + all);
            Assert.Contains("fix ok", all);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Exporter_NeverThrows_OnBadPaths()
    {
        string dir = Path.Combine(Path.GetTempPath(), "beam-diag-test-" + Guid.NewGuid().ToString("N"));
        var exporter = new DiagnosticsBundleExporter();

        // A log "file" that is actually a directory → read fails → skipped, zip still written.
        string zipPath = exporter.Export(dir, "s", new[] { dir }, null, User);

        Assert.True(File.Exists(zipPath));
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            Assert.Contains("summary.txt", zip.Entries.Select(e => e.Name).ToArray());
        }

        Directory.Delete(dir, recursive: true);
    }
}

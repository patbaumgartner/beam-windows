using System.IO.Compression;
using System.Text;
using Beam.Core.Diagnostics;

namespace Beam.App.Diagnostics;

/// <summary>
/// C4/UC-14: exports a diagnostics bundle (a zip) — sanitized log files, the diagnostics
/// summary, and the live status file — e.g. to the Desktop for attaching to a bug report.
///
/// A4 privacy: every text entry is run through <see cref="LogSanitizer"/> with the
/// machine/user names as secrets, so the bundle never contains them. Never throws: a
/// failed entry is skipped and logged instead.
/// </summary>
public sealed class DiagnosticsBundleExporter
{
    /// <summary>
    /// Creates <c>beam-diagnostics-&lt;timestamp&gt;.zip</c> in
    /// <paramref name="destinationDirectory"/> and returns its full path.
    /// </summary>
    /// <param name="destinationDirectory">Where the zip is written (e.g. the Desktop).</param>
    /// <param name="summary">The sanitized diagnostics summary text (entry <c>summary.txt</c>).</param>
    /// <param name="logFiles">Log files to include (beam.log, beam-detailed.jsonl); missing files are skipped.</param>
    /// <param name="statusFile">Optional live status file (status.json); skipped when missing.</param>
    /// <param name="secrets">Values stripped from every text entry (machine name, user name).</param>
    public string Export(
        string destinationDirectory,
        string summary,
        IEnumerable<string> logFiles,
        string? statusFile,
        params string[] secrets)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        ArgumentNullException.ThrowIfNull(logFiles);

        Directory.CreateDirectory(destinationDirectory);
        string zipPath = Path.Combine(
            destinationDirectory,
            $"beam-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        if (File.Exists(zipPath))
        {
            zipPath = Path.Combine(
                destinationDirectory,
                $"beam-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}.zip");
        }

        using (var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            // A4: the summary is sanitized here as well (defense in depth) — even though
            // DiagnosticsSummary.Build already sanitizes, the exporter must never write a
            // raw text entry into the bundle.
            AddTextEntry(zip, "summary.txt", LogSanitizer.Sanitize(summary, secrets));

            foreach (string file in logFiles)
            {
                AddSanitizedFileEntry(zip, file, secrets);
            }

            if (!string.IsNullOrEmpty(statusFile))
            {
                AddSanitizedFileEntry(zip, statusFile, secrets);
            }
        }

        return zipPath;
    }

    private static void AddSanitizedFileEntry(ZipArchive zip, string path, string[] secrets)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            string text = File.ReadAllText(path);
            AddTextEntry(zip, Path.GetFileName(path), LogSanitizer.Sanitize(text, secrets));
        }
        catch (Exception ex)
        {
            Log.Warn("Diagnostics bundle: skipped a file that could not be read", "Diagnostics",
                ("file", Path.GetFileName(path)), ("error", ex.Message));
        }
    }

    private static void AddTextEntry(ZipArchive zip, string entryName, string content)
    {
        try
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        catch (Exception ex)
        {
            Log.Warn("Diagnostics bundle: skipped an entry that could not be written", "Diagnostics",
                ("entry", entryName), ("error", ex.Message));
        }
    }
}

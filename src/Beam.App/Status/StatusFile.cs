using System.IO;
using System.Text.Json;
using Beam.Core.Diagnostics;
using Beam.Core.Ports;
using Beam.Core.Status;

namespace Beam.App.Status;

/// <summary>
/// B6: the <c>%LOCALAPPDATA%\Beam\status.json</c> file. The tray writes it atomically
/// (temp file + rename, exactly like the journal) on every session state change; the
/// CLI reads it with <see cref="TryRead"/>. Writes never throw — a status file must
/// never take the tray down.
/// </summary>
public sealed class StatusFile : IStatusWriter
{
    /// <summary>
    /// Maximum status file size accepted by <see cref="TryRead"/>. The file is a single
    /// small record; anything larger is corruption or an attack — refuse it.
    /// </summary>
    public const long MaxReadBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly string _tmpPath;

    public StatusFile(string? dir = null)
    {
        dir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Beam");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "status.json");
        _tmpPath = _path + ".tmp";
    }

    /// <inheritdoc />
    public void Write(StatusFileRecord record)
    {
        try
        {
            string json = JsonSerializer.Serialize(record, Options);
            File.WriteAllText(_tmpPath, json);
            // Atomic on NTFS: a single rename replaces the old status, so a crash can
            // never leave a half-written file behind.
            File.Move(_tmpPath, _path, overwrite: true);
        }
        catch
        {
            // Best-effort: a missing status file only means `beam status` reports no session.
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            if (File.Exists(_tmpPath))
            {
                File.Delete(_tmpPath);
            }
        }
        catch
        {
            // Best-effort; a stale status file is refreshed on the next transition.
        }
    }

    /// <summary>
    /// Reads the current status file, or null when none exists, it cannot be parsed, or
    /// it is larger than <see cref="MaxReadBytes"/>. Never throws.
    /// </summary>
    public static StatusFileRecord? TryRead(string? dir = null)
    {
        try
        {
            dir ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Beam");
            string path = Path.Combine(dir, "status.json");

            if (!File.Exists(path))
            {
                return null;
            }

            long length = new FileInfo(path).Length;
            if (length > MaxReadBytes)
            {
                Log.Warn("Status file too large — ignoring it", "Status",
                    ("bytes", length), ("maxBytes", MaxReadBytes));
                return null;
            }

            return JsonSerializer.Deserialize<StatusFileRecord>(File.ReadAllText(path), Options);
        }
        catch
        {
            // A corrupt status file must not break `beam status`.
            return null;
        }
    }
}

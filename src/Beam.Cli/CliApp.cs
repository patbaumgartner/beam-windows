using System.Text.Json;
using Beam.App.Journal;
using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Ports;
using Beam.Core.Startup;
using Beam.Core.Status;

namespace Beam.Cli;

/// <summary>
/// The scriptable Beam front end (UC-16), separated from process startup so every command
/// can be exercised with fake ports in unit tests.
///
/// Exit codes: 0 success, 1 failure, 2 bad arguments, 3 no projector, 4 unsupported.
/// </summary>
public sealed class CliApp
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly ITopologySource _topology;
    private readonly IDisplayModeController _mode;
    private readonly IDpiController _dpi;
    private readonly INightLightController _nightLight;
    private readonly IJournalStore _journal;
    private readonly TextWriter _out;
    private readonly TextWriter _error;
    private readonly Func<IDisposable?>? _lockFactory;

    // B6: reads the tray's live status file (status.json). Null in tests that do not
    // exercise the session block; the composition root passes StatusFile.TryRead.
    private readonly Func<StatusFileRecord?>? _statusReader;

    public CliApp(
        ITopologySource topology,
        IDisplayModeController mode,
        IDpiController dpi,
        INightLightController nightLight,
        IJournalStore journal,
        TextWriter? output = null,
        TextWriter? error = null,
        Func<IDisposable?>? lockFactory = null,
        Func<StatusFileRecord?>? statusReader = null)
    {
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _dpi = dpi ?? throw new ArgumentNullException(nameof(dpi));
        _nightLight = nightLight ?? throw new ArgumentNullException(nameof(nightLight));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _out = output ?? Console.Out;
        _error = error ?? Console.Error;
        // When null, the real single-instance lock is used; tests inject a fake so
        // parallel test runs never contend on the production Local\ mutex.
        _lockFactory = lockFactory;
        _statusReader = statusReader;
    }

    /// <summary>The CLI reports the product version from the assembly metadata.</summary>
    public static string Version =>
        typeof(CliApp).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Runs one command and returns its exit code. Never throws for expected failures.</summary>
    public int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            PrintHelp();
            return ExitCodes.InvalidArguments;
        }

        string command = args[0].ToLowerInvariant();

        CliOptions options;
        try
        {
            options = CommandLineParser.Parse(command, args.Skip(1).ToArray());
        }
        catch (CliUsageException ex)
        {
            _error.WriteLine(ex.Message);
            return ExitCodes.InvalidArguments;
        }

        // Phase A (A1): mutating commands hold the same per-user single-instance mutex as
        // the tray app for the duration of the command, so a scripted `beam apply` can
        // never race the tray's fix sequence (concurrent SetDisplayConfig / journal writes).
        IDisposable? lease = null;
        if (RequiresSingleInstanceLock(command, options))
        {
            lease = AcquireLock();
            if (lease is null)
            {
                const string msg = "Another Beam instance is holding the single-instance lock — try again in a moment.";
                if (options.Json)
                {
                    WriteJson(new { version = Version, success = false, error = msg });
                }
                else
                {
                    _error.WriteLine(msg);
                }
                return ExitCodes.Failure;
            }
        }

        try
        {
            return command switch
            {
                "help" or "-h" or "--help" => Help(options),
                "status" => Status(options),
                "devices" => Devices(options),
                "apply" => Apply(options),
                "restore" => Restore(options),
                "dpi" => Dpi(options),
                "nightlight" => NightLight(options),
                _ => InvalidCommand(command),
            };
        }
        catch (CliUsageException ex)
        {
            _error.WriteLine(ex.Message);
            return ExitCodes.InvalidArguments;
        }
        catch (Exception ex)
        {
            _error.WriteLine(ex.Message);
            return ExitCodes.Failure;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <summary>
    /// Commands that mutate display state, Night Light, or the journal must hold the
    /// single-instance lock; read-only commands (help, status, devices, dpi read) stay
    /// lock-free so monitoring and scripting never block behind the tray.
    /// </summary>
    private static bool RequiresSingleInstanceLock(string command, CliOptions options) => command switch
    {
        "apply" => true,
        "restore" => true,
        "nightlight" => true,
        "dpi" => options.Positionals.Count > 0
            && options.Positionals[0].Equals("set", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>
    /// Acquires the lock via the injected factory (the composition root passes the real
    /// <see cref="SingleInstanceLock"/>). Null means the real lock; a null return means
    /// the lock is held elsewhere and the command must be refused.
    /// </summary>
    private IDisposable? AcquireLock() =>
        _lockFactory is null ? SingleInstanceLock.TryAcquire() : _lockFactory();

    // ── Commands ────────────────────────────────────────────────────────────────

    private int Help(CliOptions options)
    {
        if (options.Json)
        {
            WriteJson(new
            {
                version = Version,
                success = true,
                commands = new[] { "status", "devices", "apply", "restore", "dpi", "nightlight" },
            });
            return ExitCodes.Success;
        }

        PrintHelp();
        return ExitCodes.Success;
    }

    private int Status(CliOptions options)
    {
        DisplayTopology topology = _topology.Query();
        bool needsFix = TriggerCondition.IsMet(topology);

        // B6: merge the running tray's live status (status.json) when present.
        StatusFileRecord? session = null;
        try
        {
            session = _statusReader?.Invoke();
        }
        catch
        {
            session = null; // an unreadable status file must not break `beam status`
        }

        var displays = topology.ActivePaths.Select(p => new
        {
            gdi = p.SourceGdiName,
            name = p.Target.FriendlyName,
            internalPanel = p.Target.IsInternal,
            source = $"{p.SourceMode?.Width ?? 0}x{p.SourceMode?.Height ?? 0}",
            signal = $"{p.TargetMode?.Width ?? 0}x{p.TargetMode?.Height ?? 0}",
            aspect = p.SourceMode is null ? "?" : AspectRatio.Describe(p.SourceMode.Width, p.SourceMode.Height),
            dpi = p.DpiCurRel is null
                ? null
                : (object?)new { currentRel = p.DpiCurRel, minRel = p.DpiMinRel, maxRel = p.DpiMaxRel },
        }).ToArray();

        if (options.Json)
        {
            WriteJson(new
            {
                version = Version,
                success = true,
                state = "session",
                mirrored = topology.IsMirrored,
                aspectMismatch = topology.HasMirrorAspectMismatch,
                displays,
                needsFix,
                session = session is null
                    ? null
                    : (object?)new
                    {
                        state = session.State,
                        reason = session.Reason,
                        updatedAtUtc = session.UpdatedAtUtc,
                        pid = session.Pid,
                        appliedSourceMode = session.AppliedSourceMode,
                        appliedDpiPercent = session.AppliedDpiPercent,
                        nightLightDisabled = session.NightLightDisabled,
                    },
            });
            return ExitCodes.Success;
        }

        _out.WriteLine(
            $"Mirrored: {(topology.IsMirrored ? "YES" : "no")}  " +
            $"Aspect mismatch: {(topology.HasMirrorAspectMismatch ? "YES" : "no")}  " +
            $"Needs fix: {(needsFix ? "YES" : "no")}");
        if (session is not null)
        {
            _out.WriteLine(
                $"Session: {session.State}" +
                (session.Reason is null ? string.Empty : $" ({session.Reason})") +
                $"  pid={session.Pid}  updated={session.UpdatedAtUtc}" +
                (session.AppliedSourceMode is null ? string.Empty : $"  applied={session.AppliedSourceMode}"));
        }

        _out.WriteLine();
        _out.WriteLine("Active displays:");
        foreach (var d in displays)
        {
            _out.WriteLine(
                $"  {d.gdi}  {d.name}  {(d.internalPanel ? "(internal)" : string.Empty)}  " +
                $"{d.source}  aspect={d.aspect}  signal={d.signal}");
        }
        return ExitCodes.Success;
    }

    private int Devices(CliOptions options)
    {
        DisplayTopology topology = _topology.Query();
        var devices = topology.Paths.Select(p => new
        {
            gdi = p.SourceGdiName,
            sourceId = p.SourceId,
            targetId = p.TargetId,
            name = p.Target.FriendlyName,
            manufacturer = p.Target.Manufacturer,
            active = p.IsActive,
            internalPanel = p.Target.IsInternal,
            output = p.Target.OutputTechnology,
        }).ToArray();

        if (options.Json)
        {
            WriteJson(new { version = Version, success = true, devices });
            return ExitCodes.Success;
        }

        // Human output answers "what is connected right now?": active displays as rows,
        // inactive known targets as a single count (Windows keeps many stale entries).
        var active = devices.Where(d => d.active).ToArray();
        foreach (var dev in active)
        {
            _out.WriteLine(
                $"[active] {dev.gdi}  {dev.name}  " +
                $"({dev.manufacturer})  {dev.output}{(dev.internalPanel ? "  internal" : string.Empty)}");
        }

        int inactiveCount = devices.Length - active.Length;
        if (inactiveCount > 0)
        {
            _out.WriteLine(inactiveCount == 1
                ? "... and 1 more known output is inactive (use --json to list all)."
                : $"... and {inactiveCount} more known outputs are inactive (use --json to list all).");
        }

        if (devices.Length == 0)
        {
            _out.WriteLine("No displays detected.");
        }
        return ExitCodes.Success;
    }

    private int Apply(CliOptions options)
    {
        DisplayTopology before = _topology.Query();
        if (!TriggerCondition.IsMet(before))
        {
            if (options.Json)
            {
                WriteJson(new
                {
                    version = Version,
                    success = false,
                    error = "No projector detected (no mirrored external with aspect mismatch).",
                });
            }
            else
            {
                _error.WriteLine("No projector detected (need a mirrored external display with an aspect mismatch).");
            }
            return ExitCodes.NoProjector;
        }

        if (options.DryRun)
        {
            if (options.Json)
            {
                WriteJson(new
                {
                    version = Version,
                    success = true,
                    plan = new
                    {
                        action = "duplicate + 16:9 shared source",
                        target = "best supported 16:9",
                        nightLight = "off",
                        dpi = "auto (best readable size)",
                        before = TopologySummary(before),
                    },
                });
            }
            else
            {
                _out.WriteLine("Dry run — would apply:");
                _out.WriteLine("  mode:       duplicate + shared source 16:9 (best supported)");
                _out.WriteLine("  NightLight: off");
                _out.WriteLine("  dpi:        auto (best readable size)");
            }
            return ExitCodes.Success;
        }

        IReadOnlyList<NightLightSnapshot> snapshots = _nightLight.SnapshotAll();
        IReadOnlyList<JournalDpiEntry>? dpiEntries = CaptureDpiEntries();
        _journal.BeginApply("cli-apply", snapshots, dpiEntries);

        NightLightWriteResult nightLight = _nightLight.SetActive(false);
        string? modeError = _mode.ApplyCloneWith16x9();
        string dpiMessage = ApplyOptimalDpi();

        bool success = modeError is null && nightLight.Success;

        if (options.Json)
        {
            WriteJson(new
            {
                version = Version,
                success,
                nightLight = nightLight.Message,
                modeError,
                dpi = dpiMessage,
                after = TopologySummary(_topology.Query()),
            });
        }
        else
        {
            _out.WriteLine($"Night Light: {nightLight.Message}");
            _out.WriteLine($"Display mode: {(modeError is null ? "OK" : modeError)}");
            _out.WriteLine($"DPI: {dpiMessage}");
            _out.WriteLine("Run 'beam restore' to undo.");
        }

        return success ? ExitCodes.Success : ExitCodes.Failure;
    }

    private int Restore(CliOptions options)
    {
        JournalRecord? journal = _journal.Read();
        IReadOnlyList<NightLightSnapshot>? snapshots = JournalStore.RehydrateNightLight(journal);
        IReadOnlyList<JournalDpiEntry>? dpiEntries = JournalStore.RehydrateDpi(journal);

        NightLightWriteResult nightLight;
        if (snapshots is { Count: > 0 })
        {
            nightLight = _nightLight.Restore(snapshots);
        }
        else if (options.NightLight)
        {
            nightLight = _nightLight.SetActive(true);
        }
        else
        {
            nightLight = new NightLightWriteResult(true, "Night Light left unchanged.", Array.Empty<string>());
        }

        string? modeError = _mode.Restore();
        (bool dpiSuccess, string dpiMessage) = RestoreDpi(dpiEntries);
        bool success = modeError is null && nightLight.Success && dpiSuccess;
        if (success)
        {
            _journal.Clear();
        }

        if (options.Json)
        {
            WriteJson(new
            {
                version = Version,
                success,
                modeError,
                nightLight = nightLight.Message,
                dpi = dpiMessage,
            });
        }
        else
        {
            _out.WriteLine($"Display mode: {(modeError is null ? "restored" : modeError)}");
            _out.WriteLine($"DPI: {dpiMessage}");
            _out.WriteLine($"Night Light: {nightLight.Message}");
        }

        if (!success)
        {
            _error.WriteLine("Restore incomplete; the recovery journal was kept so you can retry.");
        }

        return success ? ExitCodes.Success : ExitCodes.Failure;
    }

    private int Dpi(CliOptions options)
    {
        if (options.Positionals.Count < 1)
        {
            throw new CliUsageException("Usage: beam dpi <read|set> [<percent>] [--device <gdi>] [--json]");
        }

        string action = options.Positionals[0].ToLowerInvariant();
        DisplayPath? path = ResolveDevice(options.Device);

        if (path is null)
        {
            _error.WriteLine(options.Device is null
                ? "No active display found."
                : $"No active display matching '--device {options.Device}'.");
            return ExitCodes.Failure;
        }

        if (action == "read")
        {
            DpiScale? scale = _dpi.Read(path);
            if (scale?.Value is null)
            {
                _error.WriteLine("Could not read DPI.");
                return ExitCodes.Failure;
            }

            if (options.Json)
            {
                WriteJson(new
                {
                    version = Version,
                    success = true,
                    current = scale.Value.Current,
                    minimum = scale.Value.Minimum,
                    maximum = scale.Value.Maximum,
                });
            }
            else
            {
                _out.WriteLine($"DPI current={scale.Value.Current}%  range=[{scale.Value.Minimum}..{scale.Value.Maximum}]");
            }
            return ExitCodes.Success;
        }

        if (action == "set")
        {
            if (options.Positionals.Count < 2 || !int.TryParse(options.Positionals[1], out int percent))
            {
                throw new CliUsageException("Usage: beam dpi set <percent>");
            }

            bool ok = _dpi.SetPercentageAndVerify(path, percent);
            if (options.Json)
            {
                WriteJson(new { version = Version, success = ok, action = "dpi.set", percent });
            }
            else
            {
                _out.WriteLine($"DPI {percent}% → {(ok ? "applied and verified" : "FAILED")}");
            }
            return ok ? ExitCodes.Success : ExitCodes.Failure;
        }

        throw new CliUsageException("Unknown dpi action: " + action);
    }

    private int NightLight(CliOptions options)
    {
        if (options.Positionals.Count < 1
            || options.Positionals[0].ToLowerInvariant() is not ("on" or "off"))
        {
            throw new CliUsageException("Usage: beam nightlight <on|off> [--json]");
        }

        bool on = options.Positionals[0].Equals("on", StringComparison.OrdinalIgnoreCase);
        NightLightWriteResult result = _nightLight.SetActive(on);

        if (options.Json)
        {
            WriteJson(new { version = Version, success = result.Success, message = result.Message });
        }
        else
        {
            _out.WriteLine($"Night Light: {result.Message}");
        }
        return result.Success ? ExitCodes.Success : ExitCodes.Failure;
    }

    private int InvalidCommand(string command)
    {
        _error.WriteLine($"Unknown command: {command}");
        _error.WriteLine("Run 'beam help' for usage.");
        return ExitCodes.InvalidArguments;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private DisplayPath? ResolveDevice(string? deviceGdi)
    {
        IReadOnlyList<DisplayPath> active = _topology.Query().ActivePaths;

        if (deviceGdi is not null)
        {
            return active.FirstOrDefault(p =>
                p.SourceGdiName.Equals(deviceGdi, StringComparison.OrdinalIgnoreCase));
        }

        return active.FirstOrDefault(p => !p.Target.IsInternal) ?? active.FirstOrDefault();
    }

    /// <summary>Roadmap 1.9: captures the original DPI percent of the shared source before any mutation.</summary>
    private IReadOnlyList<JournalDpiEntry>? CaptureDpiEntries()
    {
        try
        {
            DisplayPath? sharedSource = _topology.Query().ActivePaths.FirstOrDefault();
            if (sharedSource is null)
            {
                return null;
            }

            DpiScale? scale = _dpi.Read(sharedSource);
            if (scale?.Value is null)
            {
                return null;
            }

            return new[]
            {
                new JournalDpiEntry(sharedSource.AdapterId, sharedSource.SourceId, scale.Value.Current),
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Roadmap 1.9: restores the original DPI scales persisted in the journal.</summary>
    private (bool Success, string Message) RestoreDpi(IReadOnlyList<JournalDpiEntry>? entries)
    {
        if (entries is not { Count: > 0 })
        {
            return (true, "left unchanged (no captured scale)");
        }

        foreach (JournalDpiEntry entry in entries)
        {
            DisplayPath? path = _topology.Query().Paths.FirstOrDefault(p =>
                p.AdapterId == entry.AdapterId && p.SourceId == entry.SourceId);
            if (path is null)
            {
                continue; // source unplugged mid-restore — skip, finish the rest
            }

            DpiScale? scale = _dpi.Read(path);
            if (!_dpi.SetPercentageAndVerify(path, entry.Percent, scale?.Value))
            {
                return (false, $"restore to {entry.Percent}% NOT verified");
            }
        }

        return (true, $"restored to {string.Join(", ", entries.Select(e => e.Percent + "%"))}");
    }

    private string ApplyOptimalDpi()
    {
        try
        {
            DisplayPath? sharedSource = _topology.Query().ActivePaths.FirstOrDefault();
            if (sharedSource is null)
            {
                return "skipped (no active source)";
            }

            DpiScale? scale = _dpi.Read(sharedSource);
            if (scale?.Value is null)
            {
                return "skipped (scale unsupported)";
            }

            uint width = sharedSource.SourceMode?.Width ?? 1920;
            int target = ScalePlanner.ChooseScale(width, scale.Value);
            if (target == scale.Value.Current)
            {
                return $"already at {target}%";
            }

            return _dpi.SetPercentageAndVerify(sharedSource, target, scale.Value)
                ? $"set to {target}% (verified)"
                : $"set to {target}% NOT verified";
        }
        catch (Exception ex)
        {
            return "failed: " + ex.Message;
        }
    }

    private void WriteJson(object value) =>
        _out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private string TopologySummary(DisplayTopology topology) =>
        string.Join(", ", topology.ActivePaths.Select(
            p => $"{p.SourceGdiName}:{p.SourceMode?.Width ?? 0}x{p.SourceMode?.Height ?? 0}"));

    private void PrintHelp()
    {
        _out.WriteLine("Beam — Projector Mode for Windows. Usage:");
        _out.WriteLine();
        _out.WriteLine("  beam status [--json]");
        _out.WriteLine("  beam devices [--json]");
        _out.WriteLine("  beam apply [--dry-run] [--json]");
        _out.WriteLine("  beam restore [--nightlight] [--json]");
        _out.WriteLine("  beam dpi read [--json]");
        _out.WriteLine("  beam dpi set <percent> [--json]");
        _out.WriteLine("  beam nightlight <on|off> [--json]");
        _out.WriteLine();
        _out.WriteLine("Exit codes: 0 success, 1 failure, 2 bad arguments, 3 no projector, 4 unsupported.");
    }
}

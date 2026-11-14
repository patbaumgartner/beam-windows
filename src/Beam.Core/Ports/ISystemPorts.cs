using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Status;

namespace Beam.Core.Ports;

/// <summary>
/// Watches display connect/disconnect/topology changes. The Windows adapter polls the
/// CCD database; tests drive the event directly.
/// </summary>
public interface IDisplayChangeSource
{
    /// <summary>Fired when a change is detected. The first raise has no previous snapshot.</summary>
    event Action<ConnectionSnapshot?, ConnectionSnapshot>? Changed;

    /// <summary>Starts watching. Idempotent.</summary>
    void Start();

    /// <summary>Stops watching. Idempotent.</summary>
    void Stop();
}

/// <summary>
/// Reads and writes the real Windows Night Light toggle.
/// </summary>
public interface INightLightController
{
    /// <summary>Captures every parsable Night Light state value.</summary>
    IReadOnlyList<NightLightSnapshot> SnapshotAll();

    /// <summary>Turns Night Light on or off for every state value, verifying each write.</summary>
    NightLightWriteResult SetActive(bool active);

    /// <summary>Restores previously captured states.</summary>
    NightLightWriteResult Restore(IReadOnlyList<NightLightSnapshot> snapshots);
}

/// <summary>
/// B3 extra: holds the system awake (display + system) while a presentation is active.
/// The Windows adapter maps this to <c>SetThreadExecutionState</c>; tests use a fake.
/// </summary>
public interface IKeepAwake
{
    /// <summary>Starts holding the display/system awake. Idempotent.</summary>
    void Hold();

    /// <summary>Releases the hold, returning to normal power behaviour. Idempotent.</summary>
    void Release();
}

/// <summary>
/// B3 extra: reads and writes the per-user "toast notifications enabled" setting
/// (Windows Focus Assist / DND). Best-effort: the Windows adapter verifies writes
/// by re-reading; tests use a fake.
/// </summary>
public interface IDndController
{
    /// <summary>Reads whether toast notifications are currently enabled (null = unknown).</summary>
    bool? SnapshotToastsEnabled();

    /// <summary>Enables or disables toast notifications, verifying the write. Returns success.</summary>
    bool SetToastsEnabled(bool enabled);
}

/// <summary>
/// B3 extra: switches the default audio render endpoint to the HDMI/projector output
/// while presenting, and restores the previous endpoint afterwards.
/// </summary>
public interface IAudioController
{
    /// <summary>
    /// Returns the id of the current default render endpoint, or null when none exists.
    /// </summary>
    string? GetDefaultRenderEndpoint();

    /// <summary>
    /// Finds an HDMI/projector-like render endpoint and makes it the default.
    /// Returns the id that was set, or null when no suitable endpoint exists.
    /// </summary>
    string? SwitchToHdmi();

    /// <summary>
    /// Restores a previously captured endpoint as the default, but only if that
    /// endpoint still exists. Returns true when the restore succeeded.
    /// </summary>
    bool Restore(string endpointId);
}

/// <summary>
/// B6: publishes the live tray session status (<c>status.json</c>) on every session
/// state change, so the CLI can merge it into <c>beam status</c>. The tray clears the
/// file on exit. Implementations never throw.
/// </summary>
public interface IStatusWriter
{
    /// <summary>Atomically replaces the status file with the given record.</summary>
    void Write(StatusFileRecord record);

    /// <summary>Removes the status file (tray exited cleanly).</summary>
    void Clear();
}

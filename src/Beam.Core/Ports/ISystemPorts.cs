using Beam.Core.Display;
using Beam.Core.NightLight;

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

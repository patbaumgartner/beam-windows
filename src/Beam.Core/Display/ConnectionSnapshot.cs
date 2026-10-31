namespace Beam.Core.Display;

/// <summary>
/// A snapshot of the display topology at a point in time: the active paths and their
/// source/target identities, aspect ratios, and resolutions. Used to diff before/after
/// a connect/disconnect event.
/// </summary>
/// <remarks>
/// "Active" here means the configured (not physically connected) snapshot; a physical
/// display that is turned off still appears in the active set. Use <see cref="DisplayId"/>
/// matching, not friendly names.
/// </remarks>
public sealed record ConnectionSnapshot(
    IReadOnlyList<DisplaySnapshot> Displays,
    DateTimeOffset TakenAt)
{
    /// <summary>The elapsed time since <see cref="TakenAt"/>.</summary>
    public TimeSpan AgeSince(DateTimeOffset now) => now - TakenAt;

    /// <summary>True when another snapshot has the same active set (order-insensitive).</summary>
    public bool SameDisplayIds(ConnectionSnapshot other)
    {
        if (other is null)
        {
            return false;
        }

        if (Displays.Count != other.Displays.Count)
        {
            return false;
        }

        var mine = Displays.Select(d => d.Id).OrderBy(x => x.AdapterId).ThenBy(x => x.SourceId);
        var theirs = other.Displays.Select(d => d.Id).OrderBy(x => x.AdapterId).ThenBy(x => x.SourceId);
        return mine.SequenceEqual(theirs);
    }
}

/// <summary>
/// Identifies a display source uniquely. The adapter id is the LUID formatted exactly as
/// <c>DisplayPath.AdapterId</c> (high:low), so equality maps to DisplayConfig identity.
/// </summary>
public readonly record struct DisplayId(string AdapterId, uint SourceId)
{
    public override string ToString() => $"{AdapterId}:{SourceId}";
}

/// <summary>A display snapshot: its id, pixel resolution, carried DPI and aspect ratio.</summary>
public sealed record DisplaySnapshot(
    DisplayId Id,
    uint Width,
    uint Height,
    int ScalePercent,
    string? FriendlyName)
{
    /// <summary>Width / height as double; NaN when height is 0.</summary>
    public double AspectRatio() => Width / (double)Height;

    /// <summary>True when the aspect ratio is wider than 16:9 targets allow.</summary>
    public bool HasWiderOrEqualAspect(double tolerance = 0.015) => AspectRatio() >= 16 / 9.0 - tolerance;
}

namespace Beam.Core.Display;

/// <summary>A display mode: a pixel size and a refresh rate.</summary>
public sealed record DisplayMode(uint Width, uint Height, double RefreshHz)
{
    /// <summary>Width divided by height, or 0 when the height is unknown.</summary>
    public double AspectRatio => Height == 0 ? 0 : (double)Width / Height;

    /// <summary>The pixel count, used to rank modes.</summary>
    public ulong Pixels => (ulong)Width * Height;

    public override string ToString() => RefreshHz > 0
        ? $"{Width}x{Height}@{RefreshHz:0.##}Hz"
        : $"{Width}x{Height}";
}

/// <summary>The aspect ratios Beam reasons about.</summary>
public enum AspectKind
{
    /// <summary>Not a ratio Beam recognises.</summary>
    Other = 0,

    /// <summary>16:9 (1.778), the shape of nearly every TV and projector.</summary>
    SixteenNine = 1,

    /// <summary>16:10 (1.600), the shape of many laptop panels.</summary>
    SixteenTen = 2,
}

/// <summary>Classifies and compares aspect ratios.</summary>
/// <remarks>
/// The tolerance is deliberately tight. A 1920x1200 panel (1.600) must not be mistaken
/// for 16:9, and a 1280x720 projector (1.778) must not be mistaken for 16:10. Modes are
/// integer pixel sizes, so a 1% tolerance is enough to absorb rounding such as
/// 1366x768 (1.779) while still separating the two families.
/// </remarks>
public static class AspectRatio
{
    public const double SixteenNineRatio = 16.0 / 9.0;
    public const double SixteenTenRatio = 16.0 / 10.0;
    private const double Tolerance = 0.015;

    /// <summary>Classifies a width/height pair.</summary>
    public static AspectKind Classify(uint width, uint height)
    {
        if (height == 0)
        {
            return AspectKind.Other;
        }

        double ratio = (double)width / height;

        if (Math.Abs(ratio - SixteenNineRatio) <= Tolerance)
        {
            return AspectKind.SixteenNine;
        }

        if (Math.Abs(ratio - SixteenTenRatio) <= Tolerance)
        {
            return AspectKind.SixteenTen;
        }

        return AspectKind.Other;
    }

    /// <summary>Classifies a mode.</summary>
    public static AspectKind Classify(DisplayMode mode) => Classify(mode.Width, mode.Height);

    /// <summary>
    /// True when mirroring these two sizes side by side would show bars or crop,
    /// i.e. the shapes differ.
    /// </summary>
    public static bool Mismatch(uint widthA, uint heightA, uint widthB, uint heightB)
    {
        if (heightA == 0 || heightB == 0)
        {
            return false;
        }

        double a = (double)widthA / heightA;
        double b = (double)widthB / heightB;
        return Math.Abs(a - b) > Tolerance;
    }

    /// <summary>Human-readable ratio, e.g. "16:9" or "16:10".</summary>
    public static string Describe(uint width, uint height) => Classify(width, height) switch
    {
        AspectKind.SixteenNine => "16:9",
        AspectKind.SixteenTen => "16:10",
        _ => height == 0 ? "?" : $"{(double)width / height:0.###}:1",
    };
}

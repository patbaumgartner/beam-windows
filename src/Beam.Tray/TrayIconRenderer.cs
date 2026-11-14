using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace Beam.Tray;

/// <summary>
/// B1: renders the tray icon — the brand mark plus an optional state badge — into a
/// graphics context. The badge is the glanceable signal (green = presenting, amber =
/// paused, red = safe mode); with <see cref="TrayBadge.None"/> the plain brand mark is
/// drawn, exactly as before.
/// </summary>
internal static class TrayIconRenderer
{
    // Badge colours chosen to stay readable on both light and dark taskbars: saturated
    // fills with a white ring outline (see Draw).
    public static readonly Color PresentingBadge = Color.FromArgb(0x1F, 0xB8, 0x6E); // green
    public static readonly Color PausedBadge = Color.FromArgb(0xF0, 0xA0, 0x20); // amber
    public static readonly Color SafeModeBadge = Color.FromArgb(0xE0, 0x3A, 0x3E); // red

    /// <summary>The badge fill colour for a badge kind.</summary>
    public static Color BadgeColor(TrayBadge badge) => badge switch
    {
        TrayBadge.Presenting => PresentingBadge,
        TrayBadge.Paused => PausedBadge,
        TrayBadge.SafeMode => SafeModeBadge,
        _ => Color.Transparent,
    };

    /// <summary>
    /// The Beam brand mark — the "high beam" headlight icon by I Create Stuff from
    /// The Noun Project (CC BY 3.0; see assets/ATTRIBUTION.md) — pre-coloured by
    /// installer/generate-icon.ps1 from the vector source Assets/high-beam.svg:
    /// Pantone 638C (#00AED8) fill over a deep #0080B4 halo. Loaded once from the
    /// embedded resources.
    ///
    /// The colours are baked into the PNG on purpose: runtime recoloring via a GDI+
    /// ImageAttributes colour matrix is silently ignored by System.Drawing.Common,
    /// which is exactly how the tray ended up showing the old black mark.
    /// </summary>
    private static readonly Lazy<Bitmap> BrandMark = new(LoadBrandMark);

    private static Bitmap LoadBrandMark()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Beam.Tray.Assets.high-beam.png")
            ?? throw new InvalidOperationException(
                "Embedded resource 'Beam.Tray.Assets.high-beam.png' is missing.");
        return new Bitmap(stream);
    }

    /// <summary>
    /// Draws the Beam high-beam icon into the given graphics context.
    ///
    /// The mark (see <see cref="BrandMark"/>) already carries the brand colours —
    /// the deep halo separates it from pale (light-mode) taskbars while the bright
    /// Pantone 638C fill carries it on dark ones — so it is drawn as rendered.
    /// </summary>
    public static void DrawBrand(Graphics g, int size)
    {
        var mark = BrandMark.Value;

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        g.DrawImage(mark, new Rectangle(0, 0, size, size),
            0, 0, mark.Width, mark.Height, GraphicsUnit.Pixel);
    }

    /// <summary>
    /// Draws the full tray icon: brand mark plus the state badge in the bottom-right
    /// corner (a filled disc with a white ring so it reads on light and dark taskbars).
    /// </summary>
    public static void Draw(Graphics g, int size, TrayBadge badge)
    {
        DrawBrand(g, size);

        if (badge == TrayBadge.None)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        int margin = Math.Max(2, size / 16);
        int diameter = Math.Max(10, size / 3);
        int x = size - diameter - margin;
        int y = size - diameter - margin;

        using var fill = new SolidBrush(BadgeColor(badge));
        using var ring = new Pen(Color.White, Math.Max(2f, size / 24f));
        g.FillEllipse(fill, x, y, diameter, diameter);
        g.DrawEllipse(ring, x, y, diameter, diameter);
    }
}

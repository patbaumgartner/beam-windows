using System.Drawing;
using System.Reflection;

namespace Beam.Tray.Tests;

/// <summary>
/// Guards the brand-colour fix: the tray icon must render the new cyan high-beam
/// mark (Pantone 638C #00AED8 over a deep #0080B4 halo), not the old black one.
/// The old code recoloured a black silhouette at draw time via a GDI+ colour
/// matrix, which System.Drawing.Common silently ignores — so the assets are now
/// pre-coloured by installer/generate-icon.ps1 and these tests pin both the
/// embedded source mark and the drawn result.
/// </summary>
public class TrayIconTests
{
    // Pantone 638C brand cyan, with tolerance for antialiasing.
    private const int CyanR = 0x00, CyanG = 0xAE, CyanB = 0xD8;

    private static List<Color> OpaquePixels(Bitmap bmp)
    {
        var pixels = new List<Color>();
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A > 200)
                {
                    pixels.Add(c);
                }
            }
        }

        return pixels;
    }

    private static Color DominantOpaqueColor(Bitmap bmp)
    {
        var pixels = OpaquePixels(bmp);
        Assert.NotEmpty(pixels);
        // Quantise to 32-per-channel buckets so antialiased edges don't fragment the vote.
        var bucket = pixels
            .GroupBy(c => (c.R / 32, c.G / 32, c.B / 32))
            .OrderByDescending(g => g.Count())
            .First();
        return Color.FromArgb(
            (int)bucket.Average(c => c.R),
            (int)bucket.Average(c => c.G),
            (int)bucket.Average(c => c.B));
    }

    private static void AssertBrandCyan(Color c, string what)
    {
        Assert.True(Math.Abs(c.R - CyanR) <= 40, $"{what}: expected cyan fill (R≈{CyanR}), got R={c.R}");
        Assert.True(Math.Abs(c.G - CyanG) <= 30, $"{what}: expected cyan fill (G≈{CyanG}), got G={c.G}");
        Assert.True(Math.Abs(c.B - CyanB) <= 30, $"{what}: expected cyan fill (B≈{CyanB}), got B={c.B}");
    }

    [Fact]
    public void EmbeddedBrandMark_IsPreColouredCyan_NotBlack()
    {
        using var stream = Assembly.GetAssembly(typeof(TrayService))!
            .GetManifestResourceStream("Beam.Tray.Assets.high-beam.png");
        Assert.NotNull(stream);
        using var mark = new Bitmap(stream);

        var dominant = DominantOpaqueColor(mark);
        AssertBrandCyan(dominant, "embedded high-beam.png");

        // The deep halo must be baked in as well (second-most-common opaque colour).
        var haloPixels = OpaquePixels(mark)
            .Count(c => c is { R: < 40, G: >= 100 and <= 160, B: >= 150 and <= 200 });
        Assert.True(haloPixels > 0, "embedded high-beam.png: no #0080B4 halo pixels found");
    }

    [Fact]
    public void EmbeddedExeIcon_IsPreColouredCyan_NotBlack()
    {
        using var stream = Assembly.GetAssembly(typeof(TrayService))!
            .GetManifestResourceStream("Beam.Tray.Assets.app.ico");
        Assert.NotNull(stream);
        using var icon = new Icon(stream);
        using var bmp = icon.ToBitmap();

        AssertBrandCyan(DominantOpaqueColor(bmp), "embedded app.ico");
    }

    [Theory]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(96)]
    public void DrawBeamIcon_RendersCyanAtEveryTraySize(int size)
    {
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        TrayService.DrawBeamIcon(g, size);

        AssertBrandCyan(DominantOpaqueColor(bmp), $"DrawBeamIcon({size})");
    }
}

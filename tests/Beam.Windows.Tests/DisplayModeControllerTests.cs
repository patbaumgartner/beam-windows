using Beam.Core.Display;
using Beam.Windows.Display;

namespace Beam.Windows.Tests;

/// <summary>Tests for the 16:9 mode selection used by duplicate-mode apply.</summary>
public class DisplayModeControllerTests
{
    private static DisplayMode Mode(uint w, uint h, double hz) => new(w, h, hz);

    [Fact]
    public void Rank16x9_FiltersOutNon169()
    {
        var modes = new[]
        {
            Mode(1920, 1200, 60), // 16:10 — must be excluded
            Mode(1920, 1080, 60), // 16:9
            Mode(1366, 768, 60),  // ~16:9
            Mode(3840, 2160, 60), // 16:9
        };

        var ranked = DisplayModeController.Rank16x9(modes);

        Assert.Equal(3, ranked.Count);
        Assert.All(ranked, m => Assert.Equal(AspectKind.SixteenNine, AspectRatio.Classify(m)));
    }

    [Fact]
    public void Rank16x9_PrefersMostPixels()
    {
        var modes = new[]
        {
            Mode(1920, 1080, 60),
            Mode(3840, 2160, 60),
            Mode(1280, 720, 30),
        };

        DisplayMode best = DisplayModeController.Rank16x9(modes).First();

        Assert.Equal(3840u, best.Width);
        Assert.Equal(2160u, best.Height);
    }

    [Fact]
    public void Rank16x9_LowRefreshFallback()
    {
        var modes = new[]
        {
            Mode(1280, 720, 30),
            Mode(1280, 720, 60),
            Mode(1920, 1080, 30),
        };

        // Same resolution: 60 Hz must beat 30 Hz.
        var ranked = DisplayModeController.Rank16x9(modes);
        Assert.Equal(1920u, ranked[0].Width);   // most pixels first
        Assert.Equal(60.0, ranked[1].RefreshHz); // then 60 over 30 at 1280x720
        Assert.Equal(30.0, ranked[2].RefreshHz);
    }

    [Fact]
    public void Rank16x9_Empty_WhenNo169Modes()
    {
        var modes = new[] { Mode(1920, 1200, 60), Mode(2560, 1600, 60) };
        Assert.Empty(DisplayModeController.Rank16x9(modes));
    }

    [Fact]
    public void BoundedCandidates_CapsAtMaxCandidates()
    {
        // UC-12 rule 5: no matter how many modes a driver reports, at most
        // MaxCandidates are tried per apply.
        var modes = Enumerable.Range(0, 10).Select(i => Mode(1920, 1080, 60 + i)).ToList();

        IReadOnlyList<DisplayMode> bounded = DisplayModeController.BoundedCandidates(modes);

        Assert.Equal(DisplayModeController.MaxCandidates, bounded.Count);
        Assert.Equal(modes.Take(DisplayModeController.MaxCandidates).ToList(), bounded);
    }

    [Fact]
    public void BoundedCandidates_KeepsAllWhenFewerThanCap()
    {
        var modes = new[] { Mode(1920, 1080, 60), Mode(1280, 720, 60) };

        Assert.Equal(2, DisplayModeController.BoundedCandidates(modes).Count);
        Assert.Empty(DisplayModeController.BoundedCandidates(Array.Empty<DisplayMode>()));
    }

    [Fact]
    public void RestoreTopologyFlag_ExtendWithTwoOrMoreDisplays()
    {
        // Found on real hardware: the OS rejects EXTEND with Win32 error 31 when only
        // one display is active, so restore must pick INTERNAL there.
        Assert.Equal(DisplayNative.SdcTopologyExtend, DisplayModeController.RestoreTopologyFlag(2));
        Assert.Equal(DisplayNative.SdcTopologyExtend, DisplayModeController.RestoreTopologyFlag(4));
    }

    [Fact]
    public void RestoreTopologyFlag_InternalForSingleDisplay()
    {
        Assert.Equal(DisplayNative.SdcTopologyInternal, DisplayModeController.RestoreTopologyFlag(1));
        Assert.Equal(DisplayNative.SdcTopologyInternal, DisplayModeController.RestoreTopologyFlag(0));
    }
}

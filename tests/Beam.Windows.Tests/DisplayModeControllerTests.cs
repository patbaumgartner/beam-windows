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
    public void Rank16x9_Native16x9Mode_IsBoostedToFront()
    {
        // B2/V8: the projector's EDID-native 1920x1080@60 wins over the 4K mode the
        // laptop source also supports.
        var modes = new[]
        {
            Mode(3840, 2160, 60),
            Mode(1920, 1080, 60), // EDID-native of the projector
            Mode(1280, 720, 60),
        };

        var ranked = DisplayModeController.Rank16x9(modes, Mode(1920, 1080, 60));

        Assert.Equal(1920u, ranked[0].Width);
        Assert.Equal(1080u, ranked[0].Height);
        Assert.Equal(3, ranked.Count); // reorder only — nothing dropped
    }

    [Fact]
    public void Rank16x9_Native30HzProjector_30HzVariantWinsOver60()
    {
        // A 30 Hz-only room: native 1920x1080@30 must beat the @60 variant at the
        // same resolution, otherwise the projector gets a signal it cannot show.
        var modes = new[]
        {
            Mode(1920, 1080, 60),
            Mode(1920, 1080, 30), // native
        };

        var ranked = DisplayModeController.Rank16x9(modes, Mode(1920, 1080, 30));

        Assert.Equal(30.0, ranked[0].RefreshHz);
    }

    [Fact]
    public void Rank16x9_NativeRefreshTolerance_59p94Matches60()
    {
        // The CCD API reports 60000/1001 (59.94) while the driver list says 60.
        var modes = new[] { Mode(1920, 1080, 30), Mode(1920, 1080, 60) };

        var ranked = DisplayModeController.Rank16x9(modes, Mode(1920, 1080, 60000.0 / 1001.0));

        Assert.Equal(60.0, ranked[0].RefreshHz);
    }

    [Fact]
    public void Rank16x9_NativeNot16x9_IsIgnored()
    {
        var modes = new[] { Mode(3840, 2160, 60), Mode(1920, 1080, 60) };
        var native = Mode(1920, 1200, 60); // 16:10 native panel — must not be boosted

        var ranked = DisplayModeController.Rank16x9(modes, native);

        Assert.Equal(3840u, ranked[0].Width); // unchanged pixels-first order
        Assert.DoesNotContain(ranked, m => m.Height == 1200);
    }

    [Fact]
    public void Rank16x9_NativeNotInDriverList_IsIgnored()
    {
        var modes = new[] { Mode(3840, 2160, 60), Mode(1920, 1080, 60) };

        var ranked = DisplayModeController.Rank16x9(modes, Mode(2560, 1440, 60));

        Assert.Equal(3840u, ranked[0].Width);
        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void Rank16x9_NullNativeMode_KeepsPixelsFirst()
    {
        var modes = new[] { Mode(1920, 1080, 60), Mode(3840, 2160, 60) };

        var ranked = DisplayModeController.Rank16x9(modes, null);

        Assert.Equal(3840u, ranked[0].Width);
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

    [Fact]
    public void TryRestoreMode_RejectedApply_TriesNextRefreshFallback()
    {
        var snapshot = new DisplayModeController.DevModeSnapshot(@"\\.\DISPLAY1", 1920, 1200, 60);
        var attempts = new List<uint>();

        bool restored = DisplayModeController.TryRestoreMode(snapshot, hz =>
        {
            attempts.Add(hz);
            return hz == 30; // CDS test passed at 60, but the apply was rejected.
        });

        Assert.True(restored);
        Assert.Equal(new uint[] { 60, 30 }, attempts);
    }

    [Fact]
    public void TryRestoreMode_AllRatesRejected_ReturnsFalseWithoutDuplicatingRates()
    {
        var snapshot = new DisplayModeController.DevModeSnapshot(@"\\.\DISPLAY1", 1920, 1200, 59);
        var attempts = new List<uint>();

        bool restored = DisplayModeController.TryRestoreMode(snapshot, hz =>
        {
            attempts.Add(hz);
            return false;
        });

        Assert.False(restored);
        Assert.Equal(new uint[] { 59, 60, 30 }, attempts);
    }

    [Fact]
    public void RestoreSnapshots_RetainsOnlyFailures_AndAttemptsEveryDisplay()
    {
        var first = new DisplayModeController.DevModeSnapshot(@"\\.\DISPLAY1", 1920, 1200, 60);
        var second = new DisplayModeController.DevModeSnapshot(@"\\.\DISPLAY2", 1920, 1080, 60);
        var third = new DisplayModeController.DevModeSnapshot(@"\\.\DISPLAY3", 1280, 720, 30);
        var attempted = new List<string>();

        IReadOnlyList<DisplayModeController.DevModeSnapshot> failed =
            DisplayModeController.RestoreSnapshots(new[] { first, second, third }, snap =>
            {
                attempted.Add(snap.GdiName);
                return snap == second;
            });

        Assert.Equal(new[] { first.GdiName, second.GdiName, third.GdiName }, attempted);
        Assert.Equal(new[] { first, third }, failed);
    }

    private static DisplayPath Path(string gdi, uint sourceId, bool isInternal, bool active) =>
        new(gdi, sourceId, "0001594D:00000000", sourceId + 1,
            new DisplayTarget(
                isInternal ? "Laptop" : "DELL", "SHP", 0x1551, "HDMI",
                isInternal ? @"monitor\Laptop" : @"monitor\DELL", isInternal, true),
            active,
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1080, 60),
            0, 0, "0", null, null, null, null);

    [Fact]
    public void SelectExternalPath_PicksFirstActiveExternalWithGdiName()
    {
        var topology = new DisplayTopology(new[]
        {
            Path(@"\\.\DISPLAY1", 0, isInternal: true, active: true),
            Path(@"\\.\DISPLAY2", 1, isInternal: false, active: true),
        }, DisplayTopologyId.Extend);

        DisplayPath? selected = DisplayModeController.SelectExternalPath(topology);

        Assert.NotNull(selected);
        Assert.Equal(@"\\.\DISPLAY2", selected!.SourceGdiName);
        Assert.False(selected.Target.IsInternal);
    }

    [Fact]
    public void SelectExternalPath_SkipsInternal_Inactive_AndMissingGdi()
    {
        var topology = new DisplayTopology(new[]
        {
            Path(@"\\.\DISPLAY1", 0, isInternal: true, active: true),
            Path("", 1, isInternal: false, active: true),              // external but no GDI name
            Path(@"\\.\DISPLAY3", 2, isInternal: false, active: false), // inactive
            Path(@"\\.\DISPLAY4", 3, isInternal: false, active: true),
        }, DisplayTopologyId.Extend);

        DisplayPath? selected = DisplayModeController.SelectExternalPath(topology);

        Assert.NotNull(selected);
        Assert.Equal(@"\\.\DISPLAY4", selected!.SourceGdiName);
    }

    [Fact]
    public void SelectExternalPath_NullWhenOnlyInternalOrEmpty()
    {
        var onlyInternal = new DisplayTopology(new[]
        {
            Path(@"\\.\DISPLAY1", 0, isInternal: true, active: true),
        }, DisplayTopologyId.Internal);

        Assert.Null(DisplayModeController.SelectExternalPath(onlyInternal));
        Assert.Null(DisplayModeController.SelectExternalPath(DisplayTopology.Empty));
    }
}

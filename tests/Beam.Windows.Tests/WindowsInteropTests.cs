using Beam.Core.Display;
using Beam.Windows.Display;

namespace Beam.Windows.Tests;

/// <summary>
/// Pure helpers in the Windows adapter: LUID parsing/formatting, the rational helper, and
/// DEVMODE construction. These do not touch a display, so they are safe in CI.
/// </summary>
public class WindowsInteropTests
{
    // ── LUID ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Luid_ToString_IsHighColonLow()
    {
        var luid = new DisplayNative.LUID { HighPart = 0x0001594D, LowPart = 0x0000002A };

        Assert.Equal("0001594D:0000002A", luid.ToString());
    }

    [Fact]
    public void Luid_RoundTripsThroughParse()
    {
        var luid = new DisplayNative.LUID { HighPart = 0x0001594D, LowPart = 0xDEADBEEF };

        bool ok = DisplayNative.LUID.TryParse(luid.ToString(), out DisplayNative.LUID parsed);

        Assert.True(ok);
        Assert.True(luid.adapterIdEquals(parsed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-colon")]
    [InlineData("ZZZZ:00000001")]
    [InlineData("00000001:ZZZZ")]
    public void Luid_ParseRejectsInvalidInput(string text)
    {
        Assert.False(DisplayNative.LUID.TryParse(text, out _));
    }

    [Fact]
    public void Luid_EqualityHelper_ComparesBothParts()
    {
        var a = new DisplayNative.LUID { HighPart = 1, LowPart = 2 };
        var b = new DisplayNative.LUID { HighPart = 1, LowPart = 3 };

        Assert.True(a.adapterIdEquals(new DisplayNative.LUID { HighPart = 1, LowPart = 2 }));
        Assert.False(a.adapterIdEquals(b));
    }

    // ── DISPLAYCONFIG_RATIONAL ──────────────────────────────────────────────────

    [Fact]
    public void Rational_ConvertsToDouble()
    {
        var rational = new DisplayNative.DISPLAYCONFIG_RATIONAL { Numerator = 60000, Denominator = 1001 };

        Assert.Equal(60000.0 / 1001.0, rational.ToDouble(), precision: 6);
    }

    [Fact]
    public void Rational_ZeroDenominator_IsZero()
    {
        var rational = new DisplayNative.DISPLAYCONFIG_RATIONAL { Numerator = 60, Denominator = 0 };

        Assert.Equal(0, rational.ToDouble());
    }

    [Theory]
    [InlineData(60u, 1u, "60")]
    [InlineData(60u, 0u, "60")]
    [InlineData(60000u, 1001u, "60000/1001")]
    public void Rational_ToString_OmitsTrivialDenominator(uint numerator, uint denominator, string expected)
    {
        var rational = new DisplayNative.DISPLAYCONFIG_RATIONAL
        {
            Numerator = numerator,
            Denominator = denominator,
        };

        Assert.Equal(expected, rational.ToString());
    }

    // ── DEVMODE ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NewDevMode_HasStructSizeAndZeroFields()
    {
        DisplayNative.DEVMODE devMode = DisplayInventory.NewDevMode();

        Assert.Equal(
            (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DisplayNative.DEVMODE>(),
            devMode.dmSize);
        Assert.Equal(string.Empty, devMode.dmDeviceName);
        Assert.Equal(0u, devMode.dmFields);
    }

    [Fact]
    public void NewDevMode_ReturnsIndependentInstances()
    {
        DisplayNative.DEVMODE first = DisplayInventory.NewDevMode();
        DisplayNative.DEVMODE second = DisplayInventory.NewDevMode();

        first.dmPelsWidth = 1920;

        Assert.Equal(0u, second.dmPelsWidth);
    }

    [Theory]
    [InlineData(0x104D, "SHP")]
    [InlineData(0xAC10, "DEL")]
    [InlineData(0, "")]
    public void DecodeEdidManufacturer_ByteSwapsNativeWord(ushort native, string expected)
    {
        Assert.Equal(expected, DisplayNative.DecodeEdidManufacturer(native));
    }

    [Theory]
    [InlineData(6, true)]  // LVDS
    [InlineData(11, true)] // DisplayPort embedded
    [InlineData(13, true)] // UDI embedded
    [InlineData(18, false)] // DisplayPort USB tunnel (USB-C) is external
    [InlineData(17, false)] // Indirect virtual
    [InlineData(10, false)]
    [InlineData(5, false)]
    public void IsInternalOutputTechnology_MatchesWingdiClassification(int value, bool expected)
    {
        Assert.Equal(expected, DisplayNative.IsInternalOutputTechnology(value));
    }

    [Fact]
    public void IsInternalOutputTechnology_InternalConstant_IsTrue()
    {
        Assert.True(DisplayNative.IsInternalOutputTechnology(DisplayNative.OutputTechnologyInternalVirtual));
    }

    [Theory]
    [InlineData(17, "Indirect virtual")]
    [InlineData(18, "DisplayPort USB tunnel")]
    public void OutputTechnologyName_UsesCorrectWingdiValues(int value, string expected)
    {
        Assert.Equal(expected, DisplayNative.OutputTechnologyName(value));
    }

    [Fact]
    public void OutputTechnologyName_InternalAndForceUdif_UseHighBitConstants()
    {
        Assert.Equal("Internal", DisplayNative.OutputTechnologyName(DisplayNative.OutputTechnologyInternalVirtual));
        Assert.Equal("Force UDIF", DisplayNative.OutputTechnologyName(DisplayNative.OutputTechnologyForceUdif));
    }

    [Theory]
    [InlineData(1, "0")]
    [InlineData(2, "90")]
    [InlineData(3, "180")]
    [InlineData(4, "270")]
    public void RotationName_MatchesDisplayconfigRotation(int value, string expected)
    {
        Assert.Equal(expected, DisplayNative.RotationName(value));
    }

    [Fact]
    public void SdcFlags_NoOptimizationAndSaveToDatabase_AreDistinct()
    {
        Assert.Equal(0x100u, DisplayNative.SdcNoOptimization);
        Assert.Equal(0x200u, DisplayNative.SdcSaveToDatabase);
    }

    [Fact]
    public void ResolveDpiPercent_UsesWindowsLadder()
    {
        Assert.Equal(225, DisplayInventory.ResolveDpiPercent(-7, -2, 2));
        Assert.Null(DisplayInventory.ResolveDpiPercent(int.MinValue, 0, 0));
    }

    // ── WindowsDpiController ────────────────────────────────────────────────────

    [Fact]
    public void DpiController_NullPath_IsRejected()
    {
        var controller = new WindowsDpiController();

        Assert.Throws<ArgumentNullException>(() => controller.Read(null!));
        Assert.Throws<ArgumentNullException>(() => controller.SetPercentageAndVerify(null!, 150));
    }

    [Fact]
    public void DpiController_UnparseableAdapter_ReturnsNoScale()
    {
        var controller = new WindowsDpiController();
        DisplayPath path = PathWithAdapter("not-a-luid");

        Assert.Null(controller.Read(path));
        Assert.False(controller.SetPercentageAndVerify(path, 150));
    }

    private static DisplayPath PathWithAdapter(string adapterId) =>
        new(
            @"\\.\DISPLAY1",
            0,
            adapterId,
            1,
            new DisplayTarget("Test", "DEL", 0x1234, "HDMI", @"monitor\test", true, true),
            true,
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1080, 60),
            0, 0, "0", null, null, null, null);
}

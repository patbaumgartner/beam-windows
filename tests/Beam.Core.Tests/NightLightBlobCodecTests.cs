using Beam.Core.NightLight;

namespace Beam.Core.Tests;

/// <summary>
/// Tests for the Night Light CloudStore value codec.
/// </summary>
/// <remarks>
/// The "real" fixtures are byte-for-byte values captured from Windows 11 26H2
/// (build 26300) on 2026-10-29 with Night Light off due to its schedule.
/// </remarks>
public class NightLightBlobCodecTests
{
    /// <summary>Real default value, 38 bytes, Night Light off via schedule.</summary>
    private static readonly byte[] RealDefaultOff =
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06,
        0x86, 0xE6, 0x8B, 0xD7, 0x06,
        0x2A, 0x2B, 0x0E, 0x10, 0x43, 0x42, 0x01,
        0x00, 0xC6, 0x14,
        0xAA, 0x8E, 0xD0, 0xE6, 0xD8, 0xEE, 0xD9, 0xEE, 0x01,
        0x00, 0x00, 0x00, 0x00,
    ];

    /// <summary>Real per-device value, 39 bytes, same state with a 5-byte suffix.</summary>
    private static readonly byte[] RealPerDeviceOff =
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06,
        0xD5, 0x8D, 0x8D, 0xD7, 0x06,
        0x2A, 0x2B, 0x0E, 0x11, 0x43, 0x42, 0x01,
        0x00, 0xC6, 0x14,
        0xAA, 0x8E, 0xD0, 0xE6, 0xD8, 0xEE, 0xD9, 0xEE, 0x01,
        0x01, 0x00, 0x00, 0x00, 0x00,
    ];

    [Fact]
    public void Parse_RealDefaultOff_ReadsSemanticState()
    {
        NightLightBlob blob = NightLightBlobCodec.Parse(RealDefaultOff);

        Assert.False(blob.State.Active);
        Assert.Equal(NightLightTransitionCause.Schedule, blob.State.TransitionCause);
        Assert.Equal(1793258246u, blob.State.PrologueEpochSeconds);
        Assert.Equal(134377318462523178L, blob.State.ModifiedFileTime);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, blob.Suffix);
    }

    [Fact]
    public void Parse_RealPerDeviceOff_KeepsFiveByteSuffix()
    {
        NightLightBlob blob = NightLightBlobCodec.Parse(RealPerDeviceOff);

        Assert.False(blob.State.Active);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 }, blob.Suffix);
    }

    [Fact]
    public void Serialize_TurnOn_ProducesParsableValueWithPreservedSuffix()
    {
        NightLightBlob original = NightLightBlobCodec.Parse(RealDefaultOff);
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        byte[] bytes = NightLightBlobCodec.Serialize(
            original,
            NightLightState.On(0, 0),
            now);

        NightLightBlob result = NightLightBlobCodec.Parse(bytes);

        Assert.True(result.State.Active);
        Assert.Equal(NightLightTransitionCause.Manual, result.State.TransitionCause);
        Assert.Equal(original.Suffix, result.Suffix);

        // +2 for the active marker, +3 for the manual marker.
        Assert.Equal(RealDefaultOff.Length + 5, bytes.Length);
    }

    [Fact]
    public void Serialize_TurnOffFromOn_RemovesBothMarkers()
    {
        NightLightBlob original = NightLightBlobCodec.Parse(RealDefaultOff);
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        byte[] on = NightLightBlobCodec.Serialize(original, NightLightState.On(0, 0), now);
        NightLightBlob onBlob = NightLightBlobCodec.Parse(on);

        byte[] off = NightLightBlobCodec.Serialize(
            onBlob,
            new NightLightState(false, NightLightTransitionCause.Schedule, 0, 0),
            now);

        NightLightBlob result = NightLightBlobCodec.Parse(off);

        Assert.False(result.State.Active);
        Assert.Equal(NightLightTransitionCause.Schedule, result.State.TransitionCause);
        Assert.Equal(RealDefaultOff.Length, off.Length);
    }

    [Fact]
    public void Serialize_AdvancesEpochBeyondStoredValue()
    {
        NightLightBlob original = NightLightBlobCodec.Parse(RealDefaultOff);

        // A clock far in the past must not produce an epoch Windows would revert.
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        byte[] bytes = NightLightBlobCodec.Serialize(original, NightLightState.On(0, 0), now);

        NightLightBlob result = NightLightBlobCodec.Parse(bytes);
        Assert.Equal(original.State.PrologueEpochSeconds + 2, result.State.PrologueEpochSeconds);
    }

    [Fact]
    public void Serialize_UsesWallClockWhenItIsAhead()
    {
        NightLightBlob original = NightLightBlobCodec.Parse(RealDefaultOff);
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        byte[] bytes = NightLightBlobCodec.Serialize(original, NightLightState.On(0, 0), now);

        NightLightBlob result = NightLightBlobCodec.Parse(bytes);
        Assert.Equal((uint)now.ToUnixTimeSeconds(), result.State.PrologueEpochSeconds);
    }

    [Fact]
    public void Serialize_KeepsUnknownPerDeviceSuffixBytes()
    {
        NightLightBlob original = NightLightBlobCodec.Parse(RealPerDeviceOff);
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        byte[] bytes = NightLightBlobCodec.Serialize(original, NightLightState.On(0, 0), now);
        NightLightBlob result = NightLightBlobCodec.Parse(bytes);

        Assert.Equal(original.Suffix, result.Suffix);
        Assert.Equal(RealPerDeviceOff.Length + 5, bytes.Length);
    }

    [Fact]
    public void Parse_TruncatedValue_Throws()
    {
        byte[] truncated = RealDefaultOff[..^1];

        Assert.Throws<NightLightFormatException>(() => NightLightBlobCodec.Parse(truncated));
    }

    [Fact]
    public void Parse_TrailingByte_Throws()
    {
        byte[] extended = [.. RealDefaultOff, 0x00];

        Assert.Throws<NightLightFormatException>(() => NightLightBlobCodec.Parse(extended));
    }

    [Fact]
    public void Parse_WrongMagic_Throws()
    {
        byte[] corrupted = (byte[])RealDefaultOff.Clone();
        corrupted[0] = 0x44;

        Assert.Throws<NightLightFormatException>(() => NightLightBlobCodec.Parse(corrupted));
    }

    [Fact]
    public void Parse_BodyLengthMismatch_Throws()
    {
        byte[] corrupted = (byte[])RealDefaultOff.Clone();

        // Body length is the VLQ immediately before the inner magic at offset 18.
        corrupted[18] = 0x20;

        Assert.Throws<NightLightFormatException>(() => NightLightBlobCodec.Parse(corrupted));
    }

    [Fact]
    public void Parse_EmptyValue_Throws()
    {
        Assert.Throws<NightLightFormatException>(() => NightLightBlobCodec.Parse(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Parse_DoesNotAcceptScheduleCauseWithoutManualMarker()
    {
        // Sanity check that the manual marker is what distinguishes the cause.
        NightLightBlob off = NightLightBlobCodec.Parse(RealDefaultOff);
        Assert.Equal(NightLightTransitionCause.Schedule, off.State.TransitionCause);

        NightLightBlob on = NightLightBlobCodec.Parse(
            NightLightBlobCodec.Serialize(
                off,
                NightLightState.On(0, 0),
                new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero)));

        Assert.Equal(NightLightTransitionCause.Manual, on.State.TransitionCause);
    }
}

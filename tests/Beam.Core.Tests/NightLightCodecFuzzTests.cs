using System.Buffers.Binary;
using Beam.Core.NightLight;

namespace Beam.Core.Tests;

/// <summary>
/// Fuzz-style tests: mutate real fixture bytes and assert the parser either succeeds
/// (only when the mutation is semantics-preserving) or throws NightLightFormatException.
/// Never any other exception type, never a hang, never a wrong parse without throwing.
/// </summary>
public class NightLightCodecFuzzTests
{
    private static readonly byte[] RealDefaultOff =
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06,
        0x86, 0xE6, 0x8B, 0xD7, 0x06,
        0x2A, 0x2B, 0x0E, 0x10, 0x43, 0x42, 0x01,
        0x00, 0xC6, 0x14,
        0xAA, 0x8E, 0xD0, 0xE6, 0xD8, 0xEE, 0xD9, 0xEE, 0x01,
        0x00, 0x00, 0x00, 0x00,
    ];

    /// <summary>Deterministic xorshift PRNG so failures are reproducible.</summary>
    private static IEnumerable<byte[]> Mutants(int count)
    {
        ulong seed = 0x9E3779B97F4A7C15;
        for (int i = 0; i < count; i++)
        {
            seed ^= seed << 13;
            seed ^= seed >> 7;
            seed ^= seed << 17;

            var bytes = (byte[])RealDefaultOff.Clone();
            int mutations = 1 + (int)(seed % 3);
            for (int m = 0; m < mutations; m++)
            {
                int pos = (int)((seed >> 16) % (ulong)bytes.Length);
                bytes[pos] ^= (byte)(1 + ((seed >> 32) % 255));
            }

            yield return bytes;
        }
    }

    [Fact]
    public void Fuzz_MutatedFixtures_ParseOrThrowFormatOnly()
    {
        foreach (byte[] mutant in Mutants(500))
        {
            // Act+Assert: only NightLightFormatException may escape.
            var ex = Record.Exception(() => NightLightBlobCodec.Parse(mutant));
            Assert.True(
                ex is null or NightLightFormatException,
                $"Unexpected {ex?.GetType().Name} for mutant {Convert.ToHexString(mutant)}");
        }
    }

    [Theory]
    [InlineData(0)]     // empty
    [InlineData(1)]     // one byte
    [InlineData(5)]     // inside prologue magic
    [InlineData(12)]    // mid-epoch VLQ
    [InlineData(19)]    // inside body
    [InlineData(37)]    // one byte short
    public void Fuzz_Truncations_ThrowFormatNotCrash(int length)
    {
        byte[] truncated = RealDefaultOff[..length];
        Assert.Throws<NightLightFormatException>(() => NightLightBlobCodec.Parse(truncated));
    }

    [Fact]
    public void Fuzz_AllSingleByteCorruptions_ParseOrThrowFormatOnly()
    {
        // Exhaustive single-byte corruption over a 38-byte fixture = 38*255 mutants.
        for (int pos = 0; pos < RealDefaultOff.Length; pos++)
        {
            for (int xor = 1; xor <= 255; xor++)
            {
                var bytes = (byte[])RealDefaultOff.Clone();
                bytes[pos] ^= (byte)xor;

                var ex = Record.Exception(() => NightLightBlobCodec.Parse(bytes));
                Assert.True(
                    ex is null or NightLightFormatException,
                    $"pos={pos} xor={xor} produced {ex?.GetType().Name}");
            }
        }
    }

    [Fact]
    public void Roundtrip_OnOffOn_PreservesSuffixAndIsSelfConsistent()
    {
        NightLightBlob blob = NightLightBlobCodec.Parse(RealDefaultOff);
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

        byte[] on = NightLightBlobCodec.Serialize(blob, NightLightState.On(0, 0), now);
        NightLightBlob onBlob = NightLightBlobCodec.Parse(on);
        byte[] off = NightLightBlobCodec.Serialize(onBlob, new NightLightState(false, NightLightTransitionCause.Schedule, 0, 0), now);
        NightLightBlob offBlob = NightLightBlobCodec.Parse(off);

        Assert.Equal(blob.Suffix, offBlob.Suffix);
        Assert.False(offBlob.State.Active);
    }

    [Fact]
    public void Roundtrip_MonotonicEpoch_NeverReverts()
    {
        NightLightBlob blob = NightLightBlobCodec.Parse(RealDefaultOff);
        var t0 = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

        byte[] first = NightLightBlobCodec.Serialize(blob, NightLightState.On(0, 0), t0);
        NightLightBlob firstBlob = NightLightBlobCodec.Parse(first);

        // A clock 10 years in the past must still produce an epoch > the stored one.
        byte[] second = NightLightBlobCodec.Serialize(
            firstBlob, NightLightState.Off(0, 0), t0.AddYears(-10));

        NightLightBlob secondBlob = NightLightBlobCodec.Parse(second);
        Assert.True(
            secondBlob.State.PrologueEpochSeconds > firstBlob.State.PrologueEpochSeconds,
            "epoch must be strictly increasing even with a regressed clock");
    }

    [Fact]
    public void ToHex_KnownBytes_Formats()
    {
        Assert.Equal("DE AD BE EF", NightLightBlobCodec.ToHex([0xDE, 0xAD, 0xBE, 0xEF]));
        Assert.Equal("", NightLightBlobCodec.ToHex(ReadOnlySpan<byte>.Empty));
    }
}

using System.Buffers.Binary;

namespace Beam.Core.NightLight;

/// <summary>
/// Thrown when a CloudStore Night Light value does not match the known layout.
/// </summary>
/// <remarks>
/// Beam must never guess at this format. A parse failure means "do not write".
/// </remarks>
public sealed class NightLightFormatException : Exception
{
    public NightLightFormatException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A parsed Night Light state value that can be re-serialized without losing bytes we
/// do not understand.
/// </summary>
/// <param name="State">The semantic state.</param>
/// <param name="Suffix">
/// Every byte after the VLQ-encoded FILETIME inside the body, preserved verbatim.
/// The default value ends in <c>00 00 00 00</c>; the 26H2 per-device value ends in
/// <c>01 00 00 00 00</c>. Preserving this is what keeps the per-device variant intact.
/// </param>
public sealed record NightLightBlob(NightLightState State, byte[] Suffix);

/// <summary>
/// Parser and serializer for the Windows Night Light state value.
/// </summary>
/// <remarks>
/// <para>Layout (verified against real Windows 11 26H2 values and the community
/// reverse-engineering in <c>Enyium/sem-reg-rs</c>):</para>
/// <code>
/// prologue:
///   43 42 01 00 0A [02 01] 00 2A 06 &lt;epoch VLQ&gt; 2A 2B 0E &lt;bodyLen VLQ&gt; 43 42 01
/// body:
///   00
///   [10 00]        if active
///   [D0 0A 02]     if the transition was manual
///   C6 14 &lt;FILETIME VLQ&gt;
///   &lt;suffix&gt;       (default: 00 00 00 00, per-device: 01 00 00 00 00)
/// </code>
/// <para>All VLQs are little-endian 7-bit groups with the high bit as continuation.</para>
/// </remarks>
public static class NightLightBlobCodec
{
    private static ReadOnlySpan<byte> PrologueMagic => new byte[] { 0x43, 0x42, 0x01 };
    private static ReadOnlySpan<byte> InnerMagic => new byte[] { 0x43, 0x42, 0x01 };
    private static ReadOnlySpan<byte> ActiveMarker => new byte[] { 0x10, 0x00 };
    private static ReadOnlySpan<byte> ManualMarker => new byte[] { 0xD0, 0x0A, 0x02 };
    private static ReadOnlySpan<byte> FileTimeMarker => new byte[] { 0xC6, 0x14 };

    /// <summary>
    /// Parses a state value. Throws <see cref="NightLightFormatException"/> if the
    /// bytes do not match the known layout.
    /// </summary>
    public static NightLightBlob Parse(ReadOnlySpan<byte> bytes)
    {
        var reader = new Reader(bytes);

        reader.Expect(PrologueMagic, "prologue magic");
        reader.Expect(0x00, "prologue separator");
        reader.Expect(0x0A, "prologue 0x0A");

        bool has0201 = reader.TryConsume(new byte[] { 0x02, 0x01 });
        if (!has0201)
        {
            throw new NightLightFormatException("Prologue is missing the 02 01 marker.");
        }

        reader.Expect(0x00, "prologue separator 2");
        reader.Expect(new byte[] { 0x2A, 0x06 }, "prologue epoch marker");

        ulong epoch = reader.ReadVlq();
        if (epoch > uint.MaxValue)
        {
            throw new NightLightFormatException($"Prologue epoch {epoch} does not fit in uint32.");
        }

        reader.Expect(new byte[] { 0x2A, 0x2B, 0x0E }, "prologue body-length marker");

        ulong bodyLength = reader.ReadVlq();
        if (bodyLength > 4096)
        {
            throw new NightLightFormatException($"Body length {bodyLength} is implausible.");
        }

        reader.Expect(InnerMagic, "inner magic");

        int bodyStart = reader.Position;
        reader.Expect(0x00, "body prefix");

        bool active = reader.TryConsume(ActiveMarker);
        bool manual = reader.TryConsume(ManualMarker);

        reader.Expect(FileTimeMarker, "FILETIME marker");
        ulong fileTime = reader.ReadVlq();
        if (fileTime > long.MaxValue)
        {
            throw new NightLightFormatException("FILETIME does not fit in int64.");
        }

        int consumed = reader.Position - bodyStart;
        if ((ulong)consumed > bodyLength)
        {
            throw new NightLightFormatException(
                $"Body consumed {consumed} bytes but the prologue declared {bodyLength}.");
        }

        int suffixLength = (int)bodyLength - consumed;
        if (reader.Position + suffixLength > bytes.Length)
        {
            throw new NightLightFormatException(
                $"Value ends before the declared body of {bodyLength} bytes is complete.");
        }

        byte[] suffix = bytes.Slice(reader.Position, suffixLength).ToArray();
        reader.Advance(suffixLength);

        if (reader.Position != bytes.Length)
        {
            throw new NightLightFormatException(
                $"Value has {bytes.Length - reader.Position} trailing byte(s) after the declared body.");
        }

        var state = new NightLightState(
            active,
            manual ? NightLightTransitionCause.Manual : NightLightTransitionCause.Schedule,
            (uint)epoch,
            (long)fileTime);

        return new NightLightBlob(state, suffix);
    }

    /// <summary>
    /// Re-serializes a state, advancing the prologue epoch so Windows accepts the write.
    /// </summary>
    /// <param name="blob">The parsed value (keeps the unknown suffix bytes).</param>
    /// <param name="state">The desired semantic state.</param>
    /// <param name="now">
    /// Current time. Windows reverts a write whose prologue epoch is not greater than the
    /// stored one, so the new epoch is <c>max(now, stored + 2)</c>.
    /// </param>
    public static byte[] Serialize(NightLightBlob blob, NightLightState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentNullException.ThrowIfNull(state);

        uint nowEpoch = (uint)Math.Clamp(now.ToUnixTimeSeconds(), 0, uint.MaxValue);
        uint epoch = Math.Max(nowEpoch, blob.State.PrologueEpochSeconds + 2);

        long fileTime = ToFileTime(now);

        var body = new List<byte>(32)
        {
            0x00,
        };

        if (state.Active)
        {
            body.AddRange(ActiveMarker.ToArray());
        }

        if (state.TransitionCause == NightLightTransitionCause.Manual)
        {
            body.AddRange(ManualMarker.ToArray());
        }

        body.AddRange(FileTimeMarker.ToArray());
        WriteVlq(body, (ulong)fileTime);
        body.AddRange(blob.Suffix);

        var result = new List<byte>(64);
        result.AddRange(PrologueMagic.ToArray());
        result.Add(0x00);
        result.Add(0x0A);
        result.Add(0x02);
        result.Add(0x01);
        result.Add(0x00);
        result.AddRange(new byte[] { 0x2A, 0x06 });
        WriteVlq(result, epoch);
        result.AddRange(new byte[] { 0x2A, 0x2B, 0x0E });
        WriteVlq(result, (ulong)body.Count);
        result.AddRange(InnerMagic.ToArray());
        result.AddRange(body);

        return result.ToArray();
    }

    /// <summary>Converts a timestamp to a Windows FILETIME (100 ns ticks since 1601-01-01 UTC).</summary>
    public static long ToFileTime(DateTimeOffset value)
    {
        return value.UtcDateTime.ToFileTimeUtc();
    }

    /// <summary>Formats a byte array as space-separated uppercase hex (diagnostics).</summary>
    public static string ToHex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes).Chunk(2).Aggregate(
            new System.Text.StringBuilder(),
            (sb, pair) => sb.Append(pair).Append(' ')).ToString().TrimEnd();
    }

    private static void WriteVlq(List<byte> target, ulong value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                b |= 0x80;
            }

            target.Add(b);
        }
        while (value != 0);
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;

        public Reader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            Position = 0;
        }

        public int Position { get; private set; }

        public void Advance(int count)
        {
            if (count < 0 || Position + count > _bytes.Length)
            {
                throw new NightLightFormatException("Unexpected end of value.");
            }

            Position += count;
        }

        public void Expect(byte expected, string what)
        {
            if (Position >= _bytes.Length)
            {
                throw new NightLightFormatException($"Unexpected end of value while reading {what}.");
            }

            byte actual = _bytes[Position];
            if (actual != expected)
            {
                throw new NightLightFormatException(
                    $"Expected {expected:X2} for {what} at offset {Position}, found {actual:X2}.");
            }

            Position++;
        }

        public void Expect(ReadOnlySpan<byte> expected, string what)
        {
            if (Position + expected.Length > _bytes.Length)
            {
                throw new NightLightFormatException($"Unexpected end of value while reading {what}.");
            }

            if (!_bytes.Slice(Position, expected.Length).SequenceEqual(expected))
            {
                throw new NightLightFormatException(
                    $"Unexpected bytes for {what} at offset {Position}.");
            }

            Position += expected.Length;
        }

        public bool TryConsume(ReadOnlySpan<byte> expected)
        {
            if (Position + expected.Length > _bytes.Length)
            {
                return false;
            }

            if (!_bytes.Slice(Position, expected.Length).SequenceEqual(expected))
            {
                return false;
            }

            Position += expected.Length;
            return true;
        }

        public ulong ReadVlq()
        {
            ulong value = 0;
            int shift = 0;

            while (true)
            {
                if (Position >= _bytes.Length)
                {
                    throw new NightLightFormatException("Unexpected end of value inside a VLQ.");
                }

                byte b = _bytes[Position++];
                value |= (ulong)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
                if (shift > 63)
                {
                    throw new NightLightFormatException("VLQ is too long.");
                }
            }
        }
    }
}

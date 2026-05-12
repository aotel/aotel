using System.Buffers;
using System.Runtime.CompilerServices;

namespace AOTel.Core.Parsing;

/// <summary>
/// High-performance, allocation-free decoder for Base-128 VarInts used in Protobuf.
/// </summary>
public static class VarIntDecoder
{
    /// <summary>
    /// Decodes a 64-bit unsigned integer from a SequenceReader&lt;byte&gt;.
    /// </summary>
    /// <param name="reader">The reader containing the VarInt bytes.</param>
    /// <param name="value">The decoded value.</param>
    /// <returns>True if decoding was successful; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDecode(ref SequenceReader<byte> reader, out ulong value)
    {
        value = 0;
        int shift = 0;

        var unreadSpan = reader.UnreadSpan;
        if (unreadSpan.Length >= 10)
        {
            int i = 0;
            while (i < unreadSpan.Length)
            {
                byte b = unreadSpan[i];
                value |= (ulong)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                {
                    reader.Advance(i + 1);
                    return true;
                }

                shift += 7;

                if (shift >= 70)
                {
                    break; // Overflow/Invalid varint format
                }

                i++;
            }

            value = 0;
            return false;
        }

        while (reader.TryRead(out byte b))
        {
            value |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;

            if (shift >= 70)
            {
                break; // Overflow/Invalid varint format
            }
        }

        value = 0;
        return false;
    }
}

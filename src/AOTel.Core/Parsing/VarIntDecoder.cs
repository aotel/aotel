using System;
using System.Runtime.CompilerServices;

namespace AOTel.Core.Parsing;

/// <summary>
/// High-performance, allocation-free decoder for Base-128 VarInts used in Protobuf.
/// </summary>
public readonly ref struct VarIntDecoder
{
    /// <summary>
    /// Decodes a 64-bit unsigned integer from a ReadOnlySpan<byte>.
    /// </summary>
    /// <param name="buffer">The buffer containing the VarInt bytes.</param>
    /// <returns>A tuple containing the decoded value and the number of bytes consumed. 
    /// If the buffer is incomplete or invalid, BytesConsumed will be 0.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (ulong Value, int BytesConsumed) Decode(ReadOnlySpan<byte> buffer)
    {
        ulong result = 0;
        int shift = 0;

        for (int i = 0; i < buffer.Length; i++)
        {
            byte b = buffer[i];
            
            // Mask out the MSB and shift the remaining 7 bits into the result
            result |= (ulong)(b & 0x7F) << shift;

            // If the MSB (0x80) is not set, this is the final byte of the VarInt
            if ((b & 0x80) == 0)
            {
                return (result, i + 1);
            }

            shift += 7;

            // A 64-bit integer in Base-128 encoding can take at most 10 bytes (70 bits)
            if (shift >= 70)
            {
                break; // Overflow/Invalid varint format
            }
        }

        // Indicates incomplete data or an invalid payload stream (caller should check for 0)
        return (0, 0);
    }
}
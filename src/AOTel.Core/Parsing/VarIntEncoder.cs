using System.Runtime.CompilerServices;

namespace AOTel.Core.Parsing;

/// <summary>
/// High-performance, allocation-free encoder for Base-128 VarInts used in Protobuf.
/// </summary>
public static class VarIntEncoder
{
    /// <summary>
    /// Calculates the exact number of bytes required to encode the given value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetByteCount(ulong value)
    {
        int count = 1;
        while ((value >>= 7) != 0)
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// Encodes a 64-bit unsigned integer into the provided span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Encode(ulong value, Span<byte> destination)
    {
        int bytesWritten = 0;
        while (value >= 0x80)
        {
            destination[bytesWritten++] = (byte)(value | 0x80);
            value >>= 7;
        }
        destination[bytesWritten++] = (byte)value;
        return bytesWritten;
    }
}
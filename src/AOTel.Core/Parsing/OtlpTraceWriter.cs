using System.Buffers;
using System.Buffers.Binary;
using AOTel.Core.Buffers;
using AOTel.Core.Models;

namespace AOTel.Core.Parsing;

public static class OtlpTraceWriter
{
    // C# 12+ collection expressions mapped to ReadOnlySpan point directly to the assembly .data segment (0 heap allocation)
    private static ReadOnlySpan<byte> ResourceBlockBytes => 
    [
        0x0A, 0x1F, // Field 1 (Resource), Length 31
        0x0A, 0x1D, // Field 1 (attributes), Length 29
        0x0A, 0x0C, // Field 1 (key), Length 12
        0x73, 0x65, 0x72, 0x76, 0x69, 0x63, 0x65, 0x2E, 0x6E, 0x61, 0x6D, 0x65, // "service.name"
        0x12, 0x0D, // Field 2 (value), Length 13
        0x0A, 0x0B, // Field 1 (string_value), Length 11
        0x41, 0x4F, 0x54, 0x65, 0x6C, 0x2D, 0x50, 0x72, 0x6F, 0x78, 0x79  // "AOTel-Proxy"
    ];

    private static ReadOnlySpan<byte> SpanNameBytes => 
    [
        0x1A, 0x0E, // Field 3 (name), Length 14
        0x66, 0x6F, 0x72, 0x77, 0x61, 0x72, 0x64, 0x65, 0x64, 0x2D, 0x73, 0x70, 0x61, 0x6E // "forwarded-span"
    ];

    /// <summary>
    /// Serializes a batch of telemetry spans into an exact-sized array rented from the ArrayPool.
    /// Guaranteed Native AOT compatible and 100% free of GC allocations.
    /// </summary>
    public static (byte[] Buffer, int Length) Write(TelemetryBatch batch)
    {
        if (batch.Count == 0) return (Array.Empty<byte>(), 0);

        // Constant OTLP Field Size Calculation:
        // A single span payload is exactly 62 bytes (including the hardcoded 16-byte name). 
        // When preceded by the header [0x12] (Field 2, Length-Delimited) and [0x3E] (VarInt 62), 
        // it totals exactly 64 bytes per span.
        int spansPayloadSize = batch.Count * 64;
        
        // The Resource block is hardcoded to 33 bytes.
        int resourceBlockSize = 33;

        // Calculate the sizes for the required nested parent wrappers
        int scopeSpansHeaderSize = 1 + VarIntEncoder.GetByteCount((ulong)spansPayloadSize);
        int resourceSpansPayloadSize = resourceBlockSize + scopeSpansHeaderSize + spansPayloadSize;

        int resourceSpansHeaderSize = 1 + VarIntEncoder.GetByteCount((ulong)resourceSpansPayloadSize);
        
        int totalSize = resourceSpansHeaderSize + resourceSpansPayloadSize;
        
        // Rent exactly the memory we need (or slightly more) to construct the HTTP payload
        byte[] rented = ArrayPool<byte>.Shared.Rent(totalSize);
        Span<byte> buffer = rented.AsSpan();
        int offset = 0;

        // 1. Write ResourceSpans Header
        buffer[offset++] = 0x0A; // Field 1, Length-Delimited
        offset += VarIntEncoder.Encode((ulong)resourceSpansPayloadSize, buffer.Slice(offset));

        // 2. Write Resource Block
        ResourceBlockBytes.CopyTo(buffer.Slice(offset));
        offset += 33;

        // 3. Write ScopeSpans Header
        buffer[offset++] = 0x12; // Field 2, Length-Delimited
        offset += VarIntEncoder.Encode((ulong)spansPayloadSize, buffer.Slice(offset));

        // 4. Write individual Spans dynamically
        for (int i = 0; i < batch.Count; i++)
        {
            ref readonly OtlpSpan span = ref batch.Buffer[i];

            buffer[offset++] = 0x12; // scopes_spans: spans field (2)
            buffer[offset++] = 0x3E; // span length (62)

            // Write TraceId (Field 1, 16 bytes, Big-Endian)
            buffer[offset++] = 0x0A; 
            buffer[offset++] = 0x10; 
            BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(offset, 8), span.TraceIdHigh);
            offset += 8;
            BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(offset, 8), span.TraceIdLow);
            offset += 8;

            // Write SpanId (Field 2, 8 bytes, Big-Endian)
            buffer[offset++] = 0x12; 
            buffer[offset++] = 0x08; 
            BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(offset, 8), span.SpanId);
            offset += 8;

            // Write Name (Field 3, 16 bytes overhead)
            SpanNameBytes.CopyTo(buffer.Slice(offset));
            offset += 16;

            // Write StartTimeUnixNano (Field 7, Fixed64, Little-Endian)
            buffer[offset++] = 0x39; 
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), span.StartTimeUnixNano);
            offset += 8;

            // Write EndTimeUnixNano (Field 8, Fixed64, Little-Endian)
            buffer[offset++] = 0x41; 
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), span.EndTimeUnixNano);
            offset += 8;
        }

        return (rented, totalSize);
    }
}
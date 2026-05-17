using System.Buffers;
using System.Buffers.Binary;
using AOTel.Core.Buffers;
using AOTel.Core.Models;

namespace AOTel.Core.Parsing;

/// <summary>
/// Allocation-free Protobuf writer for OTLP traces.
/// </summary>
public static class OtlpTraceWriter
{
    private const int SingleSpanPayloadSize = 62;
    private const int SpanHeaderSize = 2;
    private const int TotalBytesPerSpan = SingleSpanPayloadSize + SpanHeaderSize;

    // Pre-encoded Resource attribute "service.name" = "AOTel-Proxy"
    private static ReadOnlySpan<byte> ResourceBlockBytes =>
    [
        0x0A, 0x1F,
        0x0A, 0x1D,
        0x0A, 0x0C,
        0x73, 0x65, 0x72, 0x76, 0x69, 0x63, 0x65, 0x2E, 0x6E, 0x61, 0x6D, 0x65,
        0x12, 0x0D,
        0x0A, 0x0B,
        0x41, 0x4F, 0x54, 0x65, 0x6C, 0x2D, 0x50, 0x72, 0x6F, 0x78, 0x79
    ];

    // Pre-encoded Span name "forwarded-span"
    private static ReadOnlySpan<byte> SpanNameBytes =>
    [
        0x1A, 0x0E,
        0x66, 0x6F, 0x72, 0x77, 0x61, 0x72, 0x64, 0x65, 0x64, 0x2D, 0x73, 0x70, 0x61, 0x6E
    ];

    /// <summary>
    /// Serializes a telemetry batch into an exact-sized array rented from the ArrayPool.
    /// </summary>
    /// <param name="batch">The telemetry batch to serialize.</param>
    /// <returns>A tuple containing the rented buffer and the number of bytes written.</returns>
    public static (byte[] Buffer, int Length) Write(TelemetryBatch batch)
    {
        if (batch.Count == 0)
        {
            return (Array.Empty<byte>(), 0);
        }

        // Calculate exact buffer size needed for the Protobuf payload
        int spansPayloadSize = batch.Count * TotalBytesPerSpan;
        int resourceBlockSize = 33;

        int scopeSpansHeaderSize = 1 + VarIntEncoder.GetByteCount((ulong)spansPayloadSize);
        int resourceSpansPayloadSize = resourceBlockSize + scopeSpansHeaderSize + spansPayloadSize;
        int resourceSpansHeaderSize = 1 + VarIntEncoder.GetByteCount((ulong)resourceSpansPayloadSize);
        int totalSize = resourceSpansHeaderSize + resourceSpansPayloadSize;

        byte[] rented = ArrayPool<byte>.Shared.Rent(totalSize);
        var writer = new SpanWriter(rented.AsSpan());

        // Write Resource and Scope headers
        writer.WriteByte(0x0A);
        writer.Advance(VarIntEncoder.Encode((ulong)resourceSpansPayloadSize, writer.FreeSpan));

        writer.WriteSpan(ResourceBlockBytes);

        writer.WriteByte(0x12);
        writer.Advance(VarIntEncoder.Encode((ulong)spansPayloadSize, writer.FreeSpan));

        // Write individual spans
        for (int i = 0; i < batch.Count; i++)
        {
            ref readonly OtlpSpan span = ref batch.Buffer[i];

            writer.WriteByte(0x12);
            writer.WriteByte(0x3E);

            writer.WriteByte(0x0A);
            writer.WriteByte(0x10);
            writer.WriteUInt64BigEndian(span.TraceIdHigh);
            writer.WriteUInt64BigEndian(span.TraceIdLow);

            writer.WriteByte(0x12);
            writer.WriteByte(0x08);
            writer.WriteUInt64BigEndian(span.SpanId);

            writer.WriteSpan(SpanNameBytes);

            writer.WriteByte(0x39);
            writer.WriteUInt64LittleEndian(span.StartTimeUnixNano);

            writer.WriteByte(0x41);
            writer.WriteUInt64LittleEndian(span.EndTimeUnixNano);
        }

        return (rented, totalSize);
    }

    /// <summary>
    /// Encapsulates offset tracking for sequential span writing.
    /// </summary>
    private ref struct SpanWriter
    {
        private readonly Span<byte> buffer;
        private int offset;

        public SpanWriter(Span<byte> buffer)
        {
            this.buffer = buffer;
            offset = 0;
        }

        public Span<byte> FreeSpan => buffer.Slice(offset);

        public void Advance(int count) => offset += count;

        public void WriteByte(byte b) => buffer[offset++] = b;

        public void WriteSpan(ReadOnlySpan<byte> span)
        {
            span.CopyTo(buffer.Slice(offset));
            offset += span.Length;
        }

        public void WriteUInt64BigEndian(ulong value)
        {
            BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(offset, 8), value);
            offset += 8;
        }

        public void WriteUInt64LittleEndian(ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), value);
            offset += 8;
        }
    }
}

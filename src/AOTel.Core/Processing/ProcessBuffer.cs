using System.Buffers;
using AOTel.Core.Parsing; 

namespace AOTel.Core.Processing;

/// <summary>
/// Handles the zero-allocation flattening of Kestrel's ReadOnlySequence 
/// and bridges the raw socket memory to the Protobuf reader.
/// </summary>
public static class BufferProcessor
{
    public static void Process<TProcessor>(ReadOnlySequence<byte> buffer, ref TProcessor processor) 
        where TProcessor : struct, ISpanProcessor
    {
        if (buffer.IsSingleSegment)
        {
            var otlpReader = new OtlpTraceReader(buffer.FirstSpan);
            foreach (var span in otlpReader) { processor.Process(in span); }
        }
        else
        {
            int length = (int)buffer.Length;
            if (length <= 4096) // Safe stack limit
            {
                Span<byte> stackBuffer = stackalloc byte[length];
                buffer.CopyTo(stackBuffer);
                var otlpReader = new OtlpTraceReader(stackBuffer);
                foreach (var span in otlpReader) { processor.Process(in span); }
            }
            else
            {
                // Fallback for multi-segment payloads > 4096 bytes to prevent silent data drops.
                // ArrayPool avoids GC allocations, respecting the CI memory limits.
                byte[] rented = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    buffer.CopyTo(rented);
                    var otlpReader = new OtlpTraceReader(rented.AsSpan(0, length));
                    foreach (var span in otlpReader) { processor.Process(in span); }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }
    }
}
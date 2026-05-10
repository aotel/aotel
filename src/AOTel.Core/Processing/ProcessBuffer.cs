using System.Buffers;
using AOTel.Core.Parsing; 

namespace AOTel.Core.Processing;

/// <summary>
/// Handles the zero-allocation flattening of Kestrel's ReadOnlySequence 
/// and bridges the raw socket memory to the Protobuf reader.
/// </summary>
public static class BufferProcessor
{
    public static bool Process<TProcessor>(ReadOnlySequence<byte> buffer, ref TProcessor processor) 
        where TProcessor : struct, ISpanProcessor
    {
        if (buffer.IsSingleSegment)
        {
            var otlpReader = new OtlpTraceReader(buffer.FirstSpan);
            foreach (var span in otlpReader) { processor.Process(in span); }
            return true;
        }
        else
        {
            int length = (int)buffer.Length;
            if (length > 1024 * 1024 * 5) // 5MB Hard Limit
            {
                return false;
            }

            if (length <= 4096)
            {
                Span<byte> stackBuffer = stackalloc byte[length];
                buffer.CopyTo(stackBuffer);
                var otlpReader = new OtlpTraceReader(stackBuffer);
                foreach (var span in otlpReader) { processor.Process(in span); }
            }
            else
            {
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
            return true;
        }
    }
}
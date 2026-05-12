using System.Buffers;
using AOTel.Core.Parsing;

namespace AOTel.Core.Processing;

/// <summary>
/// Handles the zero-allocation processing of Kestrel's ReadOnlySequence
/// and bridges the raw socket memory to the Protobuf reader.
/// </summary>
public static class BufferProcessor
{
    public static bool Process<TProcessor>(ReadOnlySequence<byte> buffer, ref TProcessor processor)
        where TProcessor : struct, ISpanProcessor
    {
        int length = (int)buffer.Length;
        if (length > 1024 * 1024 * 5)
        {
            return false;
        }

        var otlpReader = new OtlpTraceReader(buffer);
        foreach (var span in otlpReader)
        {
            processor.Process(in span);
        }

        return true;
    }
}

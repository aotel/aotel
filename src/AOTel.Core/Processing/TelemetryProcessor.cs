using System.Buffers;
using AOTel.Core.Buffers;
using AOTel.Core.Models;

namespace AOTel.Core.Processing;

// Struct implementation avoids heap allocation
public struct TelemetryProcessor : ISpanProcessor
{
    private readonly TelemetryBuffer buffer;
    private OtlpSpan[] rentedArray;
    private int count;

    public TelemetryProcessor(TelemetryBuffer buffer)
    {
        this.buffer = buffer;

        // Rent an array large enough for a typical incoming HTTP request batch
        rentedArray = ArrayPool<OtlpSpan>.Shared.Rent(4096);
        count = 0;
    }

    // TelemetryProcessor.cs
    public void Process(in OtlpSpan span)
    {
        if (count >= rentedArray.Length)
        {
            var newArray = ArrayPool<OtlpSpan>.Shared.Rent(rentedArray.Length * 2);
            Array.Copy(rentedArray, newArray, count);

            var oldArray = rentedArray;
            rentedArray = newArray; // Update reference FIRST

            ArrayPool<OtlpSpan>.Shared.Return(oldArray, clearArray: false);
        }

        rentedArray[count++] = span;
    }

    public void Flush()
    {
        if (count > 0)
        {
            // Handoff to the background service.
            // The exporter service is now responsible for returning the array to the pool.
            buffer.Publish(rentedArray, count);
        }
        else
        {
            // If the HTTP payload had zero valid spans, recycle the memory immediately.
            ArrayPool<OtlpSpan>.Shared.Return(rentedArray, clearArray: false);
        }
    }
}

using System.Buffers;
using AOTel.Core.Buffers;
using AOTel.Core.Models;

namespace AOTel.Core.Processing;

// Struct implementation avoids heap allocation
public struct TelemetryProcessor
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
            Flush();
            count = 0;
            rentedArray = ArrayPool<OtlpSpan>.Shared.Rent(4096);
        }

        rentedArray[count++] = span;
    }

    public void Flush()
    {
        // Prevent double returns if Flush is called multiple times or after an empty chunking
        if (rentedArray == null || rentedArray.Length == 0)
        {
            return;
        }

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

        // Clear the reference so it isn't returned to the ArrayPool again
        rentedArray = Array.Empty<OtlpSpan>();
        count = 0;
    }
}

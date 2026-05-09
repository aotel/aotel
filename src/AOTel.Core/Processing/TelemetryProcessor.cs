using System.Buffers;
using AOTel.Core.Buffers;
using AOTel.Core.Models;

namespace AOTel.Core.Processing;

// Struct implementation avoids heap allocation
public struct TelemetryProcessor : ISpanProcessor
{
    private readonly TelemetryBuffer _buffer;
    private OtlpSpan[] _rentedArray;
    private int _count;

    public TelemetryProcessor(TelemetryBuffer buffer)
    {
        _buffer = buffer;
        // Rent an array large enough for a typical incoming HTTP request batch
        _rentedArray = ArrayPool<OtlpSpan>.Shared.Rent(4096);
        _count = 0;
    }

    public void Process(in OtlpSpan span)
    {
        // If a massive single request exceeds our rent, dynamically resize the array
        if (_count >= _rentedArray.Length)
        {
            var newArray = ArrayPool<OtlpSpan>.Shared.Rent(_rentedArray.Length * 2);
            Array.Copy(_rentedArray, newArray, _count);
            ArrayPool<OtlpSpan>.Shared.Return(_rentedArray, clearArray: false);
            _rentedArray = newArray;
        }

        _rentedArray[_count++] = span;
    }

    public void Flush()
    {
        if (_count > 0)
        {
            // Handoff to the background service. 
            // The exporter service is now responsible for returning the array to the pool.
            _buffer.Publish(_rentedArray, _count);
        }
        else
        {
            // If the HTTP payload had zero valid spans, recycle the memory immediately.
            ArrayPool<OtlpSpan>.Shared.Return(_rentedArray, clearArray: false);
        }
    }
}
using AOTel.Core.Models;

namespace AOTel.Core.Buffers;

/// <summary>
/// A readonly struct wrapping our rented batch.
/// Since ArrayPool&lt;T&gt;.Rent() returns an array that might be larger than requested,
/// tracking the exact valid item count is critical for downstream consumers.
/// </summary>
public readonly struct TelemetryBatch
{
    // Provide an explicit empty state to prevent null-reference logic in the hot path
    public static readonly TelemetryBatch Empty = new(Array.Empty<OtlpSpan>(), 0);

    public OtlpSpan[] Buffer { get; }

    public int Count { get; }

    public TelemetryBatch(OtlpSpan[] buffer, int count)
    {
        Buffer = buffer;
        Count = count;
    }
}

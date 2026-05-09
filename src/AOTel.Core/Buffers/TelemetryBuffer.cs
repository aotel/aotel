using System.Buffers;
using System.Threading.Channels;
using AOTel.Core.Models;

namespace AOTel.Core.Buffers;

/// <summary>
/// A readonly struct wrapping our rented batch.
/// Since ArrayPool<T>.Rent() returns an array that might be larger than requested,
/// tracking the exact valid item count is critical for downstream consumers.
/// </summary>
public readonly struct TelemetryBatch
{
    // FIX: Provide an explicit empty state to prevent null-reference logic in the hot path
    public static readonly TelemetryBatch Empty = new(Array.Empty<OtlpSpan>(), 0);
    public OtlpSpan[] Buffer { get; }
    public int Count { get; }

    public TelemetryBatch(OtlpSpan[] buffer, int count)
    {
        Buffer = buffer;
        Count = count;
    }
}

public sealed class TelemetryBuffer
{
    private readonly Channel<TelemetryBatch> _channel;
    
    // FIX: Zero-allocation drop tracking for operational visibility
    private long _droppedBatches;
    public long DroppedBatches => Interlocked.Read(ref _droppedBatches);

    public TelemetryBuffer(int maxCapacityBatches)
    {
        var options = new BoundedChannelOptions(maxCapacityBatches)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false, 
            SingleReader = true,  
            AllowSynchronousContinuations = false 
        };

        _channel = Channel.CreateBounded<TelemetryBatch>(options, itemDropped: batch =>
        {
            // FIX: Increment atomic counter whenever a batch is dropped
            Interlocked.Increment(ref _droppedBatches);
            if (batch.Buffer != null)
            {
                ArrayPool<OtlpSpan>.Shared.Return(batch.Buffer, clearArray: false);
            }
        });
    }

    // FIX: Signal background service to drain and exit cleanly
    public void Shutdown() => _channel.Writer.TryComplete();

    public ChannelReader<TelemetryBatch> Reader => _channel.Reader;

    public void Publish(OtlpSpan[] rentedArray, int validItemCount)
    {
        var batch = new TelemetryBatch(rentedArray, validItemCount);
        _channel.Writer.TryWrite(batch);
    }

    public void ReturnBatch(TelemetryBatch batch)
    {
        if (batch.Buffer != null)
        {
            ArrayPool<OtlpSpan>.Shared.Return(batch.Buffer, clearArray: false);
        }
    }
}
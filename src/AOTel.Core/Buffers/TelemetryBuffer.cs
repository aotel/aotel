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
    public OtlpSpan[] Buffer { get; }
    public int Count { get; }

    public TelemetryBatch(OtlpSpan[] buffer, int count)
    {
        Buffer = buffer;
        Count = count;
    }
}

/// <summary>
/// High-throughput, zero-allocation ring buffer for telemetry payloads.
/// Employs an MPSC (Multiple-Writer, Single-Reader) architecture suitable for Kestrel.
/// </summary>
public sealed class TelemetryBuffer
{
    private readonly Channel<TelemetryBatch> _channel;

    public TelemetryBuffer(int maxCapacityBatches)
    {
        var options = new BoundedChannelOptions(maxCapacityBatches)
        {
            // DropOldest enables our robust backpressure strategy. It prevents OOMs 
            // by discarding stale telemetry if the backend consumer falls behind.
            FullMode = BoundedChannelFullMode.DropOldest,
            
            // Multiple Kestrel requests can write concurrently.
            SingleWriter = false, 
            
            // A dedicated background draining service will be reading.
            SingleReader = true,  
            
            // Disallowing synchronous continuations ensures that Kestrel's hot path ingestion 
            // threads are never blocked or hijacked by reader processing continuations.
            AllowSynchronousContinuations = false 
        };

        // CRITICAL FOR ZERO-ALLOCATION: 
        // When the channel drops a batch due to backpressure, we hook into the itemDropped 
        // callback to return the rented array directly back to the ArrayPool.
        _channel = Channel.CreateBounded<TelemetryBatch>(options, itemDropped: batch =>
        {
            if (batch.Buffer != null)
            {
                // We do not need to clear the array because OtlpSpan only contains primitives (ulong)
                ArrayPool<OtlpSpan>.Shared.Return(batch.Buffer, clearArray: false);
            }
        });
    }

    /// <summary>
    /// Exposes the reader for the background drain service.
    /// </summary>
    public ChannelReader<TelemetryBatch> Reader => _channel.Reader;

    /// <summary>
    /// Publishes a rented array into the buffer.
    /// Guaranteed synchronous and allocation-free because FullMode is DropOldest.
    /// </summary>
    public void Publish(OtlpSpan[] rentedArray, int validItemCount)
    {
        var batch = new TelemetryBatch(rentedArray, validItemCount);
        
        // TryWrite never blocks and always succeeds with DropOldest.
        _channel.Writer.TryWrite(batch);
    }

    /// <summary>
    /// The background draining service MUST call this after processing a batch to recycle the memory.
    /// </summary>
    public void ReturnBatch(TelemetryBatch batch)
    {
        if (batch.Buffer != null)
        {
            ArrayPool<OtlpSpan>.Shared.Return(batch.Buffer, clearArray: false);
        }
    }
}
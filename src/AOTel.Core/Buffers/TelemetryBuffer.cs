using System.Buffers;
using System.Threading.Channels;
using AOTel.Core.Models;

namespace AOTel.Core.Buffers;
public sealed class TelemetryBuffer
{
    private readonly Channel<TelemetryBatch> _channel;
    
    // Zero-allocation drop tracking for operational visibility
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
            // Zero-allocation drop tracking for operational visibility
            Interlocked.Increment(ref _droppedBatches);
            if (batch.Buffer != null)
            {
                ArrayPool<OtlpSpan>.Shared.Return(batch.Buffer, clearArray: false);
            }
        });
    }

    // Zero-allocation shutdown signaling for clean exit
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
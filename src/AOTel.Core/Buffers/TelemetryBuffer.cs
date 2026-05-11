using System.Buffers;
using System.Threading.Channels;
using AOTel.Core.Models;

namespace AOTel.Core.Buffers;

public sealed class TelemetryBuffer
{
    private readonly Channel<TelemetryBatch> channel;

    // Zero-allocation drop tracking for operational visibility
    private long droppedBatches;

    public long DroppedBatches => Interlocked.Read(ref droppedBatches);

    public static void ReturnBatch(TelemetryBatch batch)
    {
        if (batch.Buffer != null)
        {
            ArrayPool<OtlpSpan>.Shared.Return(batch.Buffer, clearArray: false);
        }
    }

    public TelemetryBuffer(int maxCapacityBatches)
    {
        var options = new BoundedChannelOptions(maxCapacityBatches)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        };

        channel = Channel.CreateBounded<TelemetryBatch>(options, itemDropped: batch =>
        {
            // Zero-allocation drop tracking for operational visibility
            Interlocked.Increment(ref droppedBatches);
            if (batch.Buffer != null)
            {
                ArrayPool<OtlpSpan>.Shared.Return(batch.Buffer, clearArray: false);
            }
        });
    }

    // Zero-allocation shutdown signaling for clean exit
    public void Shutdown() => channel.Writer.TryComplete();

    public ChannelReader<TelemetryBatch> Reader => channel.Reader;

    public void Publish(OtlpSpan[] rentedArray, int validItemCount)
    {
        var batch = new TelemetryBatch(rentedArray, validItemCount);
        channel.Writer.TryWrite(batch);
    }
}

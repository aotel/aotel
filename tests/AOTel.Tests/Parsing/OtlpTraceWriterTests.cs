using System.Buffers;
using AOTel.Core.Buffers;
using AOTel.Core.Models;
using AOTel.Core.Parsing;

namespace AOTel.Tests.Parsing;

public class OtlpTraceWriterTests
{
    [Fact]
    public void Write_EmptyBatch_ReturnsZeroLengthBuffer()
    {
        var (buffer, length) = OtlpTraceWriter.Write(TelemetryBatch.Empty);

        try
        {
            Assert.Equal(0, length);
            Assert.Empty(buffer);
        }
        finally
        {
            if (buffer != null && buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    [Fact]
    public void Write_SingleSpan_ProducesValidProtobufPayload()
    {
        var rentedSpans = ArrayPool<OtlpSpan>.Shared.Rent(1);
        rentedSpans[0] = new OtlpSpan
        {
            TraceIdHigh = 0x0102030405060708ul,
            TraceIdLow = 0x090A0B0C0D0E0F10ul,
            SpanId = 0x1112131415161718ul,
            StartTimeUnixNano = 0x191A1B1C1D1E1F20ul,
            EndTimeUnixNano = 0x2122232425262728ul,
        };

        var batch = new TelemetryBatch(rentedSpans, 1);
        var (buffer, length) = OtlpTraceWriter.Write(batch);

        try
        {
            Assert.True(length > 0, "The payload length must be greater than zero.");

            // Round-trip validation using the exact written length
            var sequence = new ReadOnlySequence<byte>(buffer, 0, length);
            var reader = new OtlpTraceReader(sequence);

            int count = 0;
            foreach (var span in reader)
            {
                Assert.Equal(0x0102030405060708ul, span.TraceIdHigh);
                Assert.Equal(0x090A0B0C0D0E0F10ul, span.TraceIdLow);
                Assert.Equal(0x1112131415161718ul, span.SpanId);
                Assert.Equal(0x191A1B1C1D1E1F20ul, span.StartTimeUnixNano);
                Assert.Equal(0x2122232425262728ul, span.EndTimeUnixNano);
                count++;
            }

            Assert.Equal(1, count);
        }
        finally
        {
            if (buffer != null && buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            ArrayPool<OtlpSpan>.Shared.Return(rentedSpans);
        }
    }

    [Fact]
    public void Write_MultiSpanBatch_CorrectlyCalculatesNestedLengths()
    {
        var rentedSpans = ArrayPool<OtlpSpan>.Shared.Rent(3);
        for (int i = 0; i < 3; i++)
        {
            rentedSpans[i] = new OtlpSpan
            {
                TraceIdHigh = 0xAAAABBBBCCCCDDDDul,
                TraceIdLow = 0xEEEEFFFF00001111ul,
                SpanId = (ulong)(i + 1), // 1, 2, 3
                StartTimeUnixNano = 1000,
                EndTimeUnixNano = 2000,
            };
        }

        var batch = new TelemetryBatch(rentedSpans, 3);
        var (buffer, length) = OtlpTraceWriter.Write(batch);

        try
        {
            var sequence = new ReadOnlySequence<byte>(buffer, 0, length);
            var reader = new OtlpTraceReader(sequence);

            int count = 0;
            foreach (var span in reader)
            {
                Assert.Equal((ulong)(count + 1), span.SpanId);
                count++;
            }

            Assert.Equal(3, count);
        }
        finally
        {
            if (buffer != null && buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            ArrayPool<OtlpSpan>.Shared.Return(rentedSpans);
        }
    }
}

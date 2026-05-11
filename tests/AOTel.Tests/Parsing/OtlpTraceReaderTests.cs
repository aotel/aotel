using AOTel.Core.Parsing;

namespace AOTel.Tests.Parsing;

public class OtlpTraceReaderTests
{
    [Fact]
    public void Enumerator_ExtractsSpanCorrectly_FromRawPayload()
    {
        // Hardcoded ExportTraceServiceRequest payload
        // request -> resource_spans (1) -> scope_spans (2) -> spans (2)
        byte[] payload = [
            0x0A, 0x32, // resource_spans (Field 1, Length 50)
            0x12, 0x30, // scope_spans (Field 2, Length 48)
            0x12, 0x2E, // spans (Field 2, Length 46)

            // --- Span Data ---
            0x0A, 0x10, // trace_id (Field 1, Length 16)
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,

            0x12, 0x08, // span_id (Field 2, Length 8)
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,

            0x39,       // start_time_unix_nano (Field 7, Fixed64)
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // 0x0102030405060708 little-endian

            0x41,       // end_time_unix_nano (Field 8, Fixed64)
            0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11 // 0x1112131415161718 little-endian
        ];

        var reader = new OtlpTraceReader(payload);
        bool spanFound = false;

        foreach (var span in reader)
        {
            Assert.False(spanFound, "The reader should only extract exactly one span.");
            spanFound = true;

            Assert.Equal(0x0102030405060708ul, span.TraceIdHigh);
            Assert.Equal(0x090A0B0C0D0E0F10ul, span.TraceIdLow);
            Assert.Equal(0x1112131415161718ul, span.SpanId);

            Assert.Equal(0x0102030405060708ul, span.StartTimeUnixNano);
            Assert.Equal(0x1112131415161718ul, span.EndTimeUnixNano);
        }

        Assert.True(spanFound, "The reader failed to locate the span within the nested payload.");
    }
}

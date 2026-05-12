using System.Buffers;
using System.Runtime.CompilerServices;
using AOTel.Core.Models;

namespace AOTel.Core.Parsing;

/// <summary>
/// High-performance, zero-allocation forward-only Protobuf reader for OTLP Traces.
/// Operates entirely on the stack and implements the enumerator pattern for foreach loops.
/// </summary>
public ref struct OtlpTraceReader
{
    private SequenceReader<byte> root;
    private SequenceReader<byte> resource;
    private SequenceReader<byte> scope;

    public OtlpSpan Current { get; private set; }

    public OtlpTraceReader(ReadOnlySequence<byte> payload)
    {
        root = new SequenceReader<byte>(payload);
        resource = default;
        scope = default;
        Current = default;
    }

    /// <summary>
    /// Allows the reader to be used directly in a foreach loop without any boxing or interface dispatch.
    /// </summary>
    /// <returns>The enumerator for this reader.</returns>
    public readonly OtlpTraceReader GetEnumerator() => this;

    public bool MoveNext()
    {
        while (true)
        {
            // 3. Parse spans within ScopeSpans
            if (scope.Remaining > 0)
            {
                if (!VarIntDecoder.TryDecode(ref scope, out ulong tag))
                {
                    scope = default;
                    continue;
                }

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                // spans
                if (field == 2 && wireType == 2)
                {
                    if (!VarIntDecoder.TryDecode(ref scope, out ulong len) || scope.Remaining < (long)len)
                    {
                        scope = default;
                        continue;
                    }

                    var spanReader = new SequenceReader<byte>(scope.Sequence.Slice(scope.Position, (long)len));
                    scope.Advance((long)len);

                    Current = ParseSpan(ref spanReader);
                    return true;
                }

                SkipField(ref scope, wireType);
                continue;
            }

            // 2. Parse scope_spans within ResourceSpans
            if (resource.Remaining > 0)
            {
                if (!VarIntDecoder.TryDecode(ref resource, out ulong tag))
                {
                    resource = default;
                    continue;
                }

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                // scope_spans
                if (field == 2 && wireType == 2)
                {
                    if (!VarIntDecoder.TryDecode(ref resource, out ulong len) || resource.Remaining < (long)len)
                    {
                        resource = default;
                        continue;
                    }

                    scope = new SequenceReader<byte>(resource.Sequence.Slice(resource.Position, (long)len));
                    resource.Advance((long)len);
                }
                else
                {
                    SkipField(ref resource, wireType);
                }

                continue;
            }

            // 1. Parse resource_spans within the Root Request
            if (root.Remaining > 0)
            {
                if (!VarIntDecoder.TryDecode(ref root, out ulong tag))
                {
                    root = default;
                    continue;
                }

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                // resource_spans
                if (field == 1 && wireType == 2)
                {
                    if (!VarIntDecoder.TryDecode(ref root, out ulong len) || root.Remaining < (long)len)
                    {
                        root = default;
                        continue;
                    }

                    resource = new SequenceReader<byte>(root.Sequence.Slice(root.Position, (long)len));
                    root.Advance((long)len);
                }
                else
                {
                    SkipField(ref root, wireType);
                }

                continue;
            }

            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OtlpSpan ParseSpan(ref SequenceReader<byte> span)
    {
        ulong traceIdHigh = 0;
        ulong traceIdLow = 0;
        ulong spanIdVal = 0;
        ulong startTime = 0;
        ulong endTime = 0;

        while (span.Remaining > 0)
        {
            if (!VarIntDecoder.TryDecode(ref span, out ulong tag))
            {
                break;
            }

            int field = (int)(tag >> 3);
            int wireType = (int)(tag & 7);

            // trace_id
            if (field == 1 && wireType == 2)
            {
                if (!VarIntDecoder.TryDecode(ref span, out ulong len) || span.Remaining < (long)len)
                {
                    break;
                }

                if (len == 16)
                {
                    span.TryReadBigEndian(out long high);
                    span.TryReadBigEndian(out long low);
                    traceIdHigh = (ulong)high;
                    traceIdLow = (ulong)low;
                }
                else
                {
                    span.Advance((long)len);
                }
            }

            // span_id
            else if (field == 2 && wireType == 2)
            {
                if (!VarIntDecoder.TryDecode(ref span, out ulong len) || span.Remaining < (long)len)
                {
                    break;
                }

                if (len == 8)
                {
                    span.TryReadBigEndian(out long spanIdSigned);
                    spanIdVal = (ulong)spanIdSigned;
                }
                else
                {
                    span.Advance((long)len);
                }
            }

            // start_time_unix_nano (fixed64)
            else if (field == 7 && wireType == 1)
            {
                if (span.Remaining < 8)
                {
                    break;
                }

                span.TryReadLittleEndian(out long startTimeSigned);
                startTime = (ulong)startTimeSigned;
            }

            // end_time_unix_nano (fixed64)
            else if (field == 8 && wireType == 1)
            {
                if (span.Remaining < 8)
                {
                    break;
                }

                span.TryReadLittleEndian(out long endTimeSigned);
                endTime = (ulong)endTimeSigned;
            }
            else
            {
                SkipField(ref span, wireType);
            }
        }

        return new OtlpSpan
        {
            TraceIdHigh = traceIdHigh,
            TraceIdLow = traceIdLow,
            SpanId = spanIdVal,
            StartTimeUnixNano = startTime,
            EndTimeUnixNano = endTime,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SkipField(ref SequenceReader<byte> reader, int wireType)
    {
        switch (wireType)
        {
            case 0: // Varint
                VarIntDecoder.TryDecode(ref reader, out _);
                break;
            case 1: // 64-bit
                if (reader.Remaining >= 8)
                {
                    reader.Advance(8);
                }
                else
                {
                    reader.Advance(reader.Remaining);
                }

                break;
            case 2: // Length-delimited
                if (VarIntDecoder.TryDecode(ref reader, out ulong len) && reader.Remaining >= (long)len)
                {
                    reader.Advance((long)len);
                }
                else
                {
                    reader.Advance(reader.Remaining);
                }

                break;
            case 5: // 32-bit
                if (reader.Remaining >= 4)
                {
                    reader.Advance(4);
                }
                else
                {
                    reader.Advance(reader.Remaining);
                }

                break;
            default:
                // If an unknown wire type is encountered, we gracefully kill the read buffer to prevent invalid slicing
                reader.Advance(reader.Remaining);
                break;
        }
    }
}

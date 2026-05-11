using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using AOTel.Core.Models;

namespace AOTel.Core.Parsing;

/// <summary>
/// High-performance, zero-allocation forward-only Protobuf reader for OTLP Traces.
/// Operates entirely on the stack and implements the enumerator pattern for foreach loops.
/// </summary>
public ref struct OtlpTraceReader
{
    private ReadOnlySpan<byte> root;
    private ReadOnlySpan<byte> resource;
    private ReadOnlySpan<byte> scope;

    public OtlpSpan Current { get; private set; }

    public OtlpTraceReader(ReadOnlySpan<byte> payload)
    {
        root = payload;
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
            if (scope.Length > 0)
            {
                var (tag, consumed) = VarIntDecoder.Decode(scope);
                if (consumed == 0)
                {
                    scope = default;
                    continue;
                }

                scope = scope.Slice(consumed);

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                // spans
                if (field == 2 && wireType == 2)
                {
                    var (len, lenConsumed) = VarIntDecoder.Decode(scope);
                    if (lenConsumed == 0 || scope.Length - lenConsumed < (int)len)
                    {
                        scope = default;
                        continue;
                    }

                    var spanPayload = scope.Slice(lenConsumed, (int)len);
                    scope = scope.Slice(lenConsumed + (int)len);

                    Current = ParseSpan(spanPayload);
                    return true;
                }

                scope = SkipField(scope, wireType);
                continue;
            }

            // 2. Parse scope_spans within ResourceSpans
            if (resource.Length > 0)
            {
                var (tag, consumed) = VarIntDecoder.Decode(resource);
                if (consumed == 0)
                {
                    resource = default;
                    continue;
                }

                resource = resource.Slice(consumed);

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                // scope_spans
                if (field == 2 && wireType == 2)
                {
                    var (len, lenConsumed) = VarIntDecoder.Decode(resource);
                    if (lenConsumed == 0 || resource.Length - lenConsumed < (int)len)
                    {
                        resource = default;
                        continue;
                    }

                    scope = resource.Slice(lenConsumed, (int)len);
                    resource = resource.Slice(lenConsumed + (int)len);
                }
                else
                {
                    resource = SkipField(resource, wireType);
                }

                continue;
            }

            // 1. Parse resource_spans within the Root Request
            if (root.Length > 0)
            {
                var (tag, consumed) = VarIntDecoder.Decode(root);
                if (consumed == 0)
                {
                    root = default;
                    continue;
                }

                root = root.Slice(consumed);

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                // resource_spans
                if (field == 1 && wireType == 2)
                {
                    var (len, lenConsumed) = VarIntDecoder.Decode(root);
                    if (lenConsumed == 0 || root.Length - lenConsumed < (int)len)
                    {
                        root = default;
                        continue;
                    }

                    resource = root.Slice(lenConsumed, (int)len);
                    root = root.Slice(lenConsumed + (int)len);
                }
                else
                {
                    root = SkipField(root, wireType);
                }

                continue;
            }

            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OtlpSpan ParseSpan(ReadOnlySpan<byte> span)
    {
        ulong traceIdHigh = 0;
        ulong traceIdLow = 0;
        ulong spanIdVal = 0;
        ulong startTime = 0;
        ulong endTime = 0;

        while (span.Length > 0)
        {
            var (tag, consumed) = VarIntDecoder.Decode(span);
            if (consumed == 0)
            {
                break;
            }

            span = span.Slice(consumed);

            int field = (int)(tag >> 3);
            int wireType = (int)(tag & 7);

            // trace_id
            if (field == 1 && wireType == 2)
            {
                var (len, lenConsumed) = VarIntDecoder.Decode(span);
                if (lenConsumed == 0 || span.Length - lenConsumed < (int)len)
                {
                    break;
                }

                if (len == 16)
                {
                    traceIdHigh = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(lenConsumed, 8));
                    traceIdLow = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(lenConsumed + 8, 8));
                }

                span = span.Slice(lenConsumed + (int)len);
            }

            // span_id
            else if (field == 2 && wireType == 2)
            {
                var (len, lenConsumed) = VarIntDecoder.Decode(span);
                if (lenConsumed == 0 || span.Length - lenConsumed < (int)len)
                {
                    break;
                }

                if (len == 8)
                {
                    spanIdVal = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(lenConsumed, 8));
                }

                span = span.Slice(lenConsumed + (int)len);
            }

            // start_time_unix_nano (fixed64)
            else if (field == 7 && wireType == 1)
            {
                if (span.Length < 8)
                {
                    break;
                }

                startTime = BinaryPrimitives.ReadUInt64LittleEndian(span);
                span = span.Slice(8);
            }

            // end_time_unix_nano (fixed64)
            else if (field == 8 && wireType == 1)
            {
                if (span.Length < 8)
                {
                    break;
                }

                endTime = BinaryPrimitives.ReadUInt64LittleEndian(span);
                span = span.Slice(8);
            }
            else
            {
                span = SkipField(span, wireType);
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
    private static ReadOnlySpan<byte> SkipField(ReadOnlySpan<byte> span, int wireType)
    {
        switch (wireType)
        {
            case 0: // Varint
                var (_, consumed) = VarIntDecoder.Decode(span);
                return consumed == 0 ? default : span.Slice(consumed);
            case 1: // 64-bit
                return span.Length >= 8 ? span.Slice(8) : default;
            case 2: // Length-delimited
                var (len, lenConsumed) = VarIntDecoder.Decode(span);
                if (lenConsumed == 0 || span.Length - lenConsumed < (int)len)
                {
                    return default;
                }

                return span.Slice(lenConsumed + (int)len);
            case 5: // 32-bit
                return span.Length >= 4 ? span.Slice(4) : default;
            default:
                // If an unknown wire type is encountered, we gracefully kill the read buffer to prevent invalid slicing
                return default;
        }
    }
}

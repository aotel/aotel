using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace AOTel.Core.Parsing;

/// <summary>
/// Represents the extracted core fields of an OTLP Span.
/// </summary>
public readonly ref struct OtlpSpan
{
    public ReadOnlySpan<byte> TraceId { get; }
    public ReadOnlySpan<byte> SpanId { get; }
    public ulong StartTimeUnixNano { get; }
    public ulong EndTimeUnixNano { get; }

    public OtlpSpan(ReadOnlySpan<byte> traceId, ReadOnlySpan<byte> spanId, ulong startTime, ulong endTime)
    {
        TraceId = traceId;
        SpanId = spanId;
        StartTimeUnixNano = startTime;
        EndTimeUnixNano = endTime;
    }
}

/// <summary>
/// High-performance, zero-allocation forward-only Protobuf reader for OTLP Traces.
/// Operates entirely on the stack and implements the enumerator pattern for foreach loops.
/// </summary>
public ref struct OtlpTraceReader
{
    private ReadOnlySpan<byte> _root;
    private ReadOnlySpan<byte> _resource;
    private ReadOnlySpan<byte> _scope;

    public OtlpSpan Current { get; private set; }

    public OtlpTraceReader(ReadOnlySpan<byte> payload)
    {
        _root = payload;
        _resource = default;
        _scope = default;
        Current = default;
    }

    /// <summary>
    /// Allows the reader to be used directly in a foreach loop without any boxing or interface dispatch.
    /// </summary>
    public readonly OtlpTraceReader GetEnumerator() => this;

    public bool MoveNext()
    {
        while (true)
        {
            // 3. Parse spans within ScopeSpans
            if (_scope.Length > 0)
            {
                var (tag, consumed) = VarIntDecoder.Decode(_scope);
                if (consumed == 0) { _scope = default; continue; }
                _scope = _scope.Slice(consumed);

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                if (field == 2 && wireType == 2) // spans
                {
                    var (len, lenConsumed) = VarIntDecoder.Decode(_scope);
                    if (lenConsumed == 0 || _scope.Length - lenConsumed < (int)len) 
                    { 
                        _scope = default; 
                        continue; 
                    }
                    
                    var spanPayload = _scope.Slice(lenConsumed, (int)len);
                    _scope = _scope.Slice(lenConsumed + (int)len);

                    Current = ParseSpan(spanPayload);
                    return true;
                }
                
                _scope = SkipField(_scope, wireType);
                continue;
            }

            // 2. Parse scope_spans within ResourceSpans
            if (_resource.Length > 0)
            {
                var (tag, consumed) = VarIntDecoder.Decode(_resource);
                if (consumed == 0) { _resource = default; continue; }
                _resource = _resource.Slice(consumed);

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                if (field == 2 && wireType == 2) // scope_spans
                {
                    var (len, lenConsumed) = VarIntDecoder.Decode(_resource);
                    if (lenConsumed == 0 || _resource.Length - lenConsumed < (int)len) 
                    { 
                        _resource = default; 
                        continue; 
                    }
                    
                    _scope = _resource.Slice(lenConsumed, (int)len);
                    _resource = _resource.Slice(lenConsumed + (int)len);
                }
                else
                {
                    _resource = SkipField(_resource, wireType);
                }
                continue;
            }

            // 1. Parse resource_spans within the Root Request
            if (_root.Length > 0)
            {
                var (tag, consumed) = VarIntDecoder.Decode(_root);
                if (consumed == 0) { _root = default; continue; }
                _root = _root.Slice(consumed);

                int field = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                if (field == 1 && wireType == 2) // resource_spans
                {
                    var (len, lenConsumed) = VarIntDecoder.Decode(_root);
                    if (lenConsumed == 0 || _root.Length - lenConsumed < (int)len) 
                    { 
                        _root = default; 
                        continue; 
                    }
                    
                    _resource = _root.Slice(lenConsumed, (int)len);
                    _root = _root.Slice(lenConsumed + (int)len);
                }
                else
                {
                    _root = SkipField(_root, wireType);
                }
                continue;
            }

            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OtlpSpan ParseSpan(ReadOnlySpan<byte> span)
    {
        ReadOnlySpan<byte> traceId = default;
        ReadOnlySpan<byte> spanId = default;
        ulong startTime = 0;
        ulong endTime = 0;

        while (span.Length > 0)
        {
            var (tag, consumed) = VarIntDecoder.Decode(span);
            if (consumed == 0) break;
            span = span.Slice(consumed);

            int field = (int)(tag >> 3);
            int wireType = (int)(tag & 7);

            if (field == 1 && wireType == 2) // trace_id
            {
                var (len, lenConsumed) = VarIntDecoder.Decode(span);
                if (lenConsumed == 0 || span.Length - lenConsumed < (int)len) break;
                
                traceId = span.Slice(lenConsumed, (int)len);
                span = span.Slice(lenConsumed + (int)len);
            }
            else if (field == 2 && wireType == 2) // span_id
            {
                var (len, lenConsumed) = VarIntDecoder.Decode(span);
                if (lenConsumed == 0 || span.Length - lenConsumed < (int)len) break;
                
                spanId = span.Slice(lenConsumed, (int)len);
                span = span.Slice(lenConsumed + (int)len);
            }
            else if (field == 7 && wireType == 1) // start_time_unix_nano (fixed64)
            {
                if (span.Length < 8) break;
                startTime = BinaryPrimitives.ReadUInt64LittleEndian(span);
                span = span.Slice(8);
            }
            else if (field == 8 && wireType == 1) // end_time_unix_nano (fixed64)
            {
                if (span.Length < 8) break;
                endTime = BinaryPrimitives.ReadUInt64LittleEndian(span);
                span = span.Slice(8);
            }
            else
            {
                span = SkipField(span, wireType);
            }
        }

        return new OtlpSpan(traceId, spanId, startTime, endTime);
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
                if (lenConsumed == 0 || span.Length - lenConsumed < (int)len) return default;
                return span.Slice(lenConsumed + (int)len);
            case 5: // 32-bit
                return span.Length >= 4 ? span.Slice(4) : default;
            default:
                // If an unknown wire type is encountered, we gracefully kill the read buffer to prevent invalid slicing
                return default;
        }
    }
}
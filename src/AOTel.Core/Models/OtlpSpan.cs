using System.Runtime.InteropServices;

namespace AOTel.Core.Models;

/// <summary>
/// A high-performance struct representation of an OTLP Span for storage and batching.
/// By storing the primitive fields inline, we bypass the 'ref struct' stack-only constraints
/// of ReadOnlySpan&lt;byte&gt;. This allows the spans to be safely parked in the heap
/// (Channels, ArrayPools) completely free of GC allocations.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct OtlpSpan
{
    // TraceId is 16 bytes. Storing it as two 64-bit integers is the fastest
    // representation for allocation-free copying and equality checks.
    public ulong TraceIdHigh;
    public ulong TraceIdLow;

    // SpanId is 8 bytes.
    public ulong SpanId;

    // Fixed64 Unix Nano timestamps.
    public ulong StartTimeUnixNano;
    public ulong EndTimeUnixNano;
}

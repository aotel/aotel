// Interface defines the contract, but will be devirtualized by the JIT
using AOTel.Core.Models;

namespace AOTel.Core.Processing;

/// <summary>
/// Defines the contract for processing parsed telemetry spans.
/// Used as a generic constraint to enable static devirtualization in Native AOT.
/// </summary>
public interface ISpanProcessor
{
    // The 'in' modifier prevents defensive copies of the struct
    void Process(in OtlpSpan span); 
}
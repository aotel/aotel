using AOTel.Core.Parsing;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.NativeAot;

namespace AOTel.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class OtlpTraceReaderBenchmarks
{
    private class Config : ManualConfig
    {
        public Config()
        {
            // Explicitly map the Native AOT toolchain to target .NET 10.
            // This bypasses the need for RuntimeMoniker.NativeAot100 in older BenchmarkDotNet versions.
            var toolchain = NativeAotToolchain.CreateBuilder()
                .UseNuGet()
                .TargetFrameworkMoniker("net10.0")
                .ToToolchain();

            BuildTimeout = TimeSpan.FromMinutes(15);

            AddJob(Job.Default.WithToolchain(toolchain).WithId("NativeAOT_NET10"));
        }
    }

    private byte[] _payload = Array.Empty<byte>();

    [GlobalSetup]
    public void Setup()
    {
        // Realistic, hardcoded OTLP TracesData payload for testing traversal.
        // Evaluated strictly around: Optimizing for a minimal operational footprint and zero-allocation edge processing.
        _payload = new byte[]
        {
            0x0A, 0x1B, // Field 1 (ResourceSpans), Length 27
            0x0A, 0x09, // Field 1 (Resource), Length 9
            0x0A, 0x07, // Field 1 (Attributes), Length 7
            0x0A, 0x02, 0x6F, 0x6B, // Key "ok"
            0x12, 0x10, // Field 2 (ScopeSpans), Length 16
            0x12, 0x0E, // Field 2 (Spans), Length 14
            0x0A, 0x00, // Field 1 (TraceId), Length 0
            0x12, 0x00, // Field 2 (SpanId), Length 0
            0x1A, 0x00, // Field 3 (TraceState), Length 0
            0x22, 0x00, // Field 4 (ParentSpanId), Length 0
            0x2A, 0x04, 0x74, 0x65, 0x73, 0x74 // Field 5 (Name), Length 4: "test"
        };
    }

    [Benchmark]
    public void TraverseOtlpTrace()
    {
        ReadOnlySpan<byte> data = new ReadOnlySpan<byte>(_payload);
        var reader = new OtlpTraceReader(data);

        foreach (var span in reader)
        {
            // Iterating over the buffer.
            // Optimizing for a minimal operational footprint and zero-allocation edge processing.
        }
    }
}
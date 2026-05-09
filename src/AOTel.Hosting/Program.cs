using System.Net;
using System.Buffers;
using System.IO.Pipelines;
using AOTel.Core.Parsing;

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });

builder.WebHost.UseKestrelCore();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Parse("0.0.0.0"), 4318);
});

// Minimal requirement to support Endpoint Routing (MapPost) on an empty builder
builder.Services.AddRoutingCore();

var app = builder.Build();

app.MapPost("/v1/traces", async (HttpContext context) =>
{
    PipeReader reader = context.Request.BodyReader;
    // Instantiate the struct handler. It will live on the state machine stack without GC allocation.
    var processor = new TelemetryProcessor();

    while (true)
    {
        ReadResult result = await reader.ReadAsync();
        ReadOnlySequence<byte> buffer = result.Buffer;

        if (buffer.Length > 0)
        {
            ProcessBuffer(buffer, ref processor);
        }

        reader.AdvanceTo(buffer.End);

        if (result.IsCompleted)
        {
            break;
        }
    }

    // Directly manipulating the HttpContext response is the fastest, allocation-free 
    // way to return a 202 Accepted status in .NET, bypassing IResult allocations.
    context.Response.StatusCode = StatusCodes.Status202Accepted;
});

app.Run();

// Generic constraint on a struct forces the JIT compiler to completely devirtualize the calls
static void ProcessBuffer<TProcessor>(ReadOnlySequence<byte> buffer, ref TProcessor processor) 
    where TProcessor : struct, ISpanProcessor
{
    if (buffer.IsSingleSegment)
    {
        var otlpReader = new OtlpTraceReader(buffer.FirstSpan);
        foreach (var span in otlpReader) { processor.Process(in span); }
    }
    else
    {
        int length = (int)buffer.Length;
        if (length <= 4096) // Safe stack limit
        {
            Span<byte> stackBuffer = stackalloc byte[length];
            buffer.CopyTo(stackBuffer);
            var otlpReader = new OtlpTraceReader(stackBuffer);
            foreach (var span in otlpReader) { processor.Process(in span); }
        }
        else
        {
            // Fallback for multi-segment payloads > 4096 bytes to prevent silent data drops.
            // ArrayPool avoids GC allocations, respecting the CI memory limits.
            byte[] rented = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                buffer.CopyTo(rented);
                var otlpReader = new OtlpTraceReader(rented.AsSpan(0, length));
                foreach (var span in otlpReader) { processor.Process(in span); }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}

// Interface defines the contract, but will be devirtualized by the JIT
public interface ISpanProcessor
{
    void Process(in OtlpSpan span); // 'in' modifier prevents 48-byte struct defensive copies
}

// Struct implementation avoids heap allocation
public struct TelemetryProcessor : ISpanProcessor
{
    public void Process(in OtlpSpan span)
    {
        // Zero-allocation, devirtualized business logic hand-off happens here
        _ = span.TraceId; 
    }
}

// Make the implicit Program class public so test projects can access it with WebApplicationFactory
public partial class Program { }

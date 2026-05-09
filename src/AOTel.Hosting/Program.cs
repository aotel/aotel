using System.Net;
using System.Buffers;
using System.IO.Pipelines;
using AOTel.Hosting.Services;
using AOTel.Core.Processing;
using AOTel.Core.Buffers;

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });

builder.WebHost.UseKestrelCore();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Parse("0.0.0.0"), 4318);
});

// Minimal requirement to support Endpoint Routing (MapPost) on an empty builder
builder.Services.AddRoutingCore();

// Register the TelemetryBuffer and the background exporter service
builder.Services.AddSingleton(new TelemetryBuffer(maxCapacityBatches: 1024));
builder.Services.AddHostedService<OtlpExporterService>();

var app = builder.Build();

app.MapPost("/v1/traces", async (HttpContext context, TelemetryBuffer telemetryBuffer) =>
{
    PipeReader reader = context.Request.BodyReader;
    
    // Pass the buffer into our processor so it knows where to send the data
    var processor = new TelemetryProcessor(telemetryBuffer);

    try
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync();
            ReadOnlySequence<byte> seq = result.Buffer;

            if (seq.Length > 0)
            {
                BufferProcessor.Process(seq, ref processor);
            }

            reader.AdvanceTo(seq.End);

            if (result.IsCompleted)
            {
                break;
            }
        }
    }
    finally
    {
        // THE FIX: Flush the batched spans into the background channel!
        processor.Flush();
    }

    context.Response.StatusCode = StatusCodes.Status202Accepted;
});

app.Run();


// Make the implicit Program class public so test projects can access it with WebApplicationFactory
public partial class Program { }

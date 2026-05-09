using System.Net;
using System.Buffers;
using System.IO.Pipelines;
using AOTel.Hosting.Services;
using AOTel.Core.Processing;
using AOTel.Core.Buffers;

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });

builder.WebHost.UseKestrelCore();
builder.WebHost.UseSockets();
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

// Program.cs
app.MapPost("/v1/traces", async (HttpContext context, TelemetryBuffer telemetryBuffer) =>
{
    PipeReader reader = context.Request.BodyReader;
    var processor = new TelemetryProcessor(telemetryBuffer);
    try
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync();
            ReadOnlySequence<byte> seq = result.Buffer;

            if (result.IsCompleted)
            {
                if (seq.Length > 0)
                {
                    BufferProcessor.Process(seq, ref processor);
                }
                reader.AdvanceTo(seq.End);
                break;
            }
            
            // Do not consume bytes, but mark them as examined to request more network data
            reader.AdvanceTo(seq.Start, seq.End);
        }
    }
    finally
    {
        processor.Flush();
    }
    context.Response.StatusCode = StatusCodes.Status202Accepted;
});

app.Run();


// Make the implicit Program class public so test projects can access it with WebApplicationFactory
public partial class Program { }

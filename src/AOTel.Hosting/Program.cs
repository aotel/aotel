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

            if (seq.Length > 1024 * 1024 * 5)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            if (result.IsCompleted)
            {
                if (seq.Length > 0)
                {
                    if (!BufferProcessor.Process(seq, ref processor))
                    {
                        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                        return;
                    }
                }
                reader.AdvanceTo(seq.End);
                context.Response.StatusCode = StatusCodes.Status202Accepted;
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
});

app.Run();


// Make the implicit Program class public so test projects can access it with WebApplicationFactory
public partial class Program { }

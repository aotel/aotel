using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using AOTel.Core.Buffers;
using AOTel.Core.Processing;
using AOTel.Hosting.Services;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.UseKestrelCore();
builder.WebHost.UseSockets();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Parse("0.0.0.0"), 4318);
});

// Register the TelemetryBuffer and the background exporter service
builder.Services.AddSingleton(new TelemetryBuffer(maxCapacityBatches: 1024));
builder.Services.AddHostedService<OtlpExporterService>();

builder.Services.AddHttpClient<OtlpExporterService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AOTEL_UPSTREAM_URL"] ?? "http://localhost:4319");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // Prevents DNS staleness by recycling connections periodically while still
    // keeping them alive long enough to benefit from TCP connection pooling.
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),

    // Setting these maximizes throughput for high-volume concurrent exports.
    EnableMultipleHttp2Connections = true,
    MaxConnectionsPerServer = 100,
});

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

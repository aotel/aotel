using System.Buffers;
using AOTel.Core.Buffers;
using AOTel.Core.Parsing;

namespace AOTel.Hosting.Services;

/// <summary>
/// A background service dedicated to draining the TelemetryBuffer and exporting
/// the payloads over HTTP. Designed for Native AOT and high-performance throughput.
/// </summary>
public sealed class OtlpExporterService : BackgroundService
{
    private readonly TelemetryBuffer buffer;
    private readonly HttpClient httpClient;
    private readonly ILogger<OtlpExporterService> logger;

    public OtlpExporterService(HttpClient httpClient, TelemetryBuffer buffer, ILogger<OtlpExporterService> logger)
    {
        this.httpClient = httpClient;
        this.buffer = buffer;
        this.logger = logger;
    }

    private static readonly System.Net.Http.Headers.MediaTypeHeaderValue ProtobufHeader
        = new("application/x-protobuf");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in buffer.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var (rentedProtobuf, protoLength) = OtlpTraceWriter.Write(batch);
                try
                {
                    if (protoLength > 0)
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/traces")
                        {
                            Content = new ByteArrayContent(rentedProtobuf, 0, protoLength),
                        };
                        request.Content.Headers.ContentType = ProtobufHeader; // Cached

                        using var response = await httpClient.SendAsync(request, stoppingToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (rentedProtobuf != null && rentedProtobuf.Length > 0)
                    {
                        ArrayPool<byte>.Shared.Return(rentedProtobuf);
                    }
                }
            }
            finally
            {
                TelemetryBuffer.ReturnBatch(batch);
            }
        }
    }
}

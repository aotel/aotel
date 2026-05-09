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
    private readonly TelemetryBuffer _buffer;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OtlpExporterService> _logger;
    private readonly Uri _exportUri = new Uri("http://localhost:4319/v1/traces");

    public OtlpExporterService(TelemetryBuffer buffer, ILogger<OtlpExporterService> logger)
    {
        _buffer = buffer;
        _logger = logger;
        
        // High-Performance Networking Configuration:
        // SocketsHttpHandler is the most optimized HTTP handler in .NET.
        var handler = new SocketsHttpHandler
        {
            // Prevents DNS staleness by recycling connections periodically while still 
            // keeping them alive long enough to benefit from TCP connection pooling.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            
            // Setting these maximizes throughput for high-volume concurrent exports.
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 100
        };
        
        _httpClient = new HttpClient(handler);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OTLP Exporter Service is starting.");

        // Asynchronously wait for batches without blocking a thread.
        await foreach (var batch in _buffer.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var (rentedProtobuf, protoLength) = OtlpTraceWriter.Write(batch);
                
                try
                {
                    if (protoLength > 0)
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, _exportUri)
                        {
                            Content = new ByteArrayContent(rentedProtobuf, 0, protoLength)
                        };
                        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");

                        Console.WriteLine($"[Exporter] Attempting to send batch of {batch.Count} spans to Jaeger...");

                        using var response = await _httpClient.SendAsync(request, stoppingToken).ConfigureAwait(false);
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            var error = await response.Content.ReadAsStringAsync(stoppingToken).ConfigureAwait(false);
                            Console.WriteLine($"[Exporter] Jaeger REJECTED the payload: {response.StatusCode} - {error}");
                            _logger.LogWarning("Failed to export telemetry batch. Status code: {StatusCode}", response.StatusCode);
                        }
                        else
                        {
                            Console.WriteLine("[Exporter] Jaeger ACCEPTED the payload! 🚀");
                        }
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "A network error occurred while exporting the telemetry batch.");
            }
            finally
            {
                // CRITICAL FOR ZERO-ALLOCATION SAFETY:
                // Always return the rented array back to the ArrayPool.
                // If we miss this on network failures, the entire ArrayPool will eventually exhaust.
                _buffer.ReturnBatch(batch);
            }
        }
    }
    
    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }
}
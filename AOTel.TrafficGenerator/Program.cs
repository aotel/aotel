using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

Console.WriteLine("🚀 Starting AOTel Traffic Generator...");

// 1. Configure the official SDK to point at your proxy
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("AOTel-TestClient"))
    .AddSource("AOTel.Test")
    .AddOtlpExporter(opt => 
    {
        // Pointing to YOUR proxy ingestion port
        opt.Endpoint = new Uri("http://localhost:4318/v1/traces");
        opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
    })
    .Build();

var source = new ActivitySource("AOTel.Test");

// 2. Generate a valid Span
using (var activity = source.StartActivity("Epic3-Validation-Span"))
{
    activity?.SetTag("status", "zero-allocation-achieved");
    activity?.SetTag("epic", "3");
    
    Console.WriteLine($"✅ Generated TraceId: {activity?.TraceId}");
    Console.WriteLine($"✅ Generated SpanId:  {activity?.SpanId}");
}

// 3. Force the HTTP flush to your proxy
tracerProvider.ForceFlush();
Console.WriteLine("📡 Payload fired at http://localhost:4318/v1/traces");
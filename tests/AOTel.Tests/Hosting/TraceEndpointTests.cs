using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AOTel.Tests.Hosting;

public class TraceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TraceEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Post_V1Traces_WithValidOtlpPayload_ReturnsAccepted()
    {
        // Arrange: Spin up the TestServer client
        var client = factory.CreateClient();

        // Realistic, hardcoded OTLP TracesData payload for testing traversal
        byte[] payload =
        [
            0x0A, 0x1B, 0x0A, 0x09, 0x0A, 0x07, 0x0A, 0x02, 0x6F, 0x6B,
            0x12, 0x10, 0x12, 0x0E, 0x0A, 0x00, 0x12, 0x00, 0x1A, 0x00,
            0x22, 0x00, 0x2A, 0x04, 0x74, 0x65, 0x73, 0x74
        ];

        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        // Act: Send the POST request to the ingestion endpoint
        var response = await client.PostAsync("/v1/traces", content);

        // Assert: Verify that the high-performance pipeline processed it and returned 202 Accepted
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}

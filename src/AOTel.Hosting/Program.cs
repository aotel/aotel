using System.Net;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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

    while (true)
    {
        ReadResult result = await reader.ReadAsync();
        ReadOnlySequence<byte> buffer = result.Buffer;

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

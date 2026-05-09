using System.Net;
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

app.MapPost("/v1/traces", (HttpContext context) =>
{
    // Directly manipulating the HttpContext response is the fastest, allocation-free 
    // way to return a 202 Accepted status in .NET, bypassing IResult allocations.
    context.Response.StatusCode = StatusCodes.Status202Accepted;
    return Task.CompletedTask;
});

app.Run();

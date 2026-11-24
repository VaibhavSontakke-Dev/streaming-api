using StreamingAutomation.MediaServer.Endpoints;
using StreamingAutomation.MediaServer.Options;
using StreamingAutomation.MediaServer.Services;

// Test Media Server: a controlled, local stand-in for a streaming platform's media delivery.
// Sessions "play" by downloading time-paced segments from here. No real platform is contacted.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<MediaServerOptions>()
    .Bind(builder.Configuration.GetSection(MediaServerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MediaCatalogue>();
builder.Services.AddSingleton<FaultInjector>();
builder.Services.AddSingleton<StreamStatistics>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.MapGet("/", () => Results.Ok(new { service = "Test Media Server", note = "Synthetic media for authorised load testing only." }));
app.MapMediaEndpoints();
app.MapHealthChecks("/health");

app.Run();

namespace StreamingAutomation.MediaServer
{
    /// <summary>Marker type for WebApplicationFactory in integration tests.</summary>
    public sealed class MediaServerEntryPoint;
}

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Infrastructure;
using StreamingAutomation.MediaServer;

namespace StreamingAutomation.IntegrationTests.Media;

/// <summary>Hosts the Test Media Server in-memory and builds the real (resilient) media client against it.</summary>
public sealed class MediaServerHost : IDisposable
{
    private readonly WebApplicationFactory<MediaServerEntryPoint> _factory;
    private readonly ServiceProvider _clientServices;

    public MediaServerHost(double transientErrorRate = 0, int maxRetries = 2)
    {
        _factory = new WebApplicationFactory<MediaServerEntryPoint>().WithWebHostBuilder(b =>
        {
            b.UseSetting("MediaServer:TransientErrorRate", transientErrorRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.UseSetting("MediaServer:BaseLatencyMs", "0");
            b.UseSetting("MediaServer:LatencyJitterMs", "0");
        });
        _ = _factory.Server; // start

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MediaClient:BaseUrl"] = "http://localhost",
            ["MediaClient:MaxRetryAttempts"] = maxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["MediaClient:RetryBaseDelayMs"] = "10",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediaClient(config);
        services.AddHttpClient<IMediaStreamClient, Infrastructure.Media.MediaStreamClient>()
            .ConfigurePrimaryHttpMessageHandler(() => _factory.Server.CreateHandler());
        _clientServices = services.BuildServiceProvider();
    }

    public IMediaStreamClient Client => _clientServices.GetRequiredService<IMediaStreamClient>();

    public HttpClient RawClient() => _factory.CreateClient();

    public HttpMessageHandler CreateHandler() => _factory.Server.CreateHandler();

    public void Dispose()
    {
        _clientServices.Dispose();
        _factory.Dispose();
    }
}

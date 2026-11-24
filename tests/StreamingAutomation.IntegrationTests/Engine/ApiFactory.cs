using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StreamingAutomation.API;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Infrastructure.Media;
using StreamingAutomation.Infrastructure.Persistence;
using StreamingAutomation.IntegrationTests.Media;

namespace StreamingAutomation.IntegrationTests.Engine;

/// <summary>
/// Boots the real API (engine, hosted services, EF Core) against a dedicated PostgreSQL test database
/// and an in-memory Test Media Server. The base connection string comes from the API's user-secrets
/// or the ConnectionStrings__Default environment variable; the database name is replaced.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<ApiEntryPoint>, IAsyncLifetime
{
    public const string TestDatabase = "streaming_automation_tests";

    public ApiFactory()
    {
        var baseConfig = new ConfigurationBuilder()
            .AddUserSecrets("streaming-automation-api")
            .AddEnvironmentVariables()
            .Build();
        var baseConnection = baseConfig.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Default or the API user-secret to run integration tests.");

        ConnectionString = new NpgsqlConnectionStringBuilder(baseConnection) { Database = TestDatabase }.ConnectionString;
    }

    public string ConnectionString { get; }

    public MediaServerHost Media { get; } = new();

    public async Task InitializeAsync()
    {
        await using var db = NewDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        Media.Dispose();
    }

    public AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options);

    /// <summary>Waits until the engine has finalized the run (removed it from the registry).</summary>
    public async Task WaitForRunToFinishAsync(Guid runId, TimeSpan timeout)
    {
        var registry = Services.GetRequiredService<RunRegistry>();
        using var cts = new CancellationTokenSource(timeout);
        while (registry.TryGet(runId, out _))
        {
            await Task.Delay(100, cts.Token);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration): Program.cs reads configuration while registering
        // services, before late-bound configuration sources from the factory are applied.
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("Playback:FlushIntervalMs", "200");
        builder.UseSetting("Playback:BroadcastIntervalMs", "500");
        builder.UseSetting("MediaClient:RetryBaseDelayMs", "10");
        builder.UseSetting("MediaClient:BaseUrl", "http://localhost");
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<IMediaStreamClient, MediaStreamClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Media.CreateHandler()));
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

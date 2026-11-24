using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using StreamingAutomation.Application.Common.Interfaces;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Infrastructure.Media;
using StreamingAutomation.Infrastructure.Persistence;

namespace StreamingAutomation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is not configured. Set ConnectionStrings__Default or use dotnet user-secrets.");

        // Pooled factory: background workers create short-lived contexts; request handlers get a scoped one.
        services.AddPooledDbContextFactory<AppDbContext>(o => ConfigureDbContext(o, connectionString));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<IPlaybackStateWriter, PlaybackStateWriter>();
        services.AddMediaClient(configuration);
        return services;
    }

    public static IServiceCollection AddMediaClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MediaClientOptions>()
            .Bind(configuration.GetSection(MediaClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IMediaStreamClient, MediaStreamClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MediaClientOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan; // timeouts are owned by the resilience pipeline
            })
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                MaxConnectionsPerServer = sp.GetRequiredService<IOptions<MediaClientOptions>>().Value.MaxConnections,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                AutomaticDecompression = DecompressionMethods.None,
            })
            .AddResilienceHandler("media-stream", static (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<MediaClientOptions>>().Value;

                // Order matters: total timeout wraps retries, which wrap the per-attempt timeout.
                pipeline.AddTimeout(TimeSpan.FromSeconds(options.TotalTimeoutSeconds));
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true, // avoids synchronized retry storms across 100+ sessions
                    Delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMs),
                    OnRetry = args =>
                    {
                        if (args.Context.GetRequestMessage()?.Options.TryGetValue(MediaStreamClient.RetryCounterKey, out var counter) == true)
                        {
                            counter.Increment();
                        }

                        return default;
                    },
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));

                // Deliberately no circuit breaker: all sessions share one client, and a breaker opened by
                // injected faults in a few sessions would fail every healthy session too.
            });

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
            })
            .UseSnakeCaseNamingConvention();
}

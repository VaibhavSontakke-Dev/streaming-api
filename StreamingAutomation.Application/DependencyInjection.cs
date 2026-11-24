using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamingAutomation.Application.Common.Options;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Application.Reports;
using StreamingAutomation.Application.Runs;

namespace StreamingAutomation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PlaybackOptions>()
            .Bind(configuration.GetSection(PlaybackOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddValidatorsFromAssemblyContaining<CreateRunRequestValidator>();
        services.AddSingleton(TimeProvider.System);

        // Engine (singletons: shared in-memory state for all runs)
        services.AddSingleton<RunRegistry>();
        services.AddSingleton<PlaybackPersistence>();
        services.AddSingleton<SessionWorker>();
        services.AddSingleton<IPlaybackOrchestrator, PlaybackOrchestrator>();

        // Hosted services stop in reverse order: lifecycle (interrupt runs) stops first,
        // then the watchdog, then the flusher performs the final write.
        services.AddHostedService<PersistenceFlushService>();
        services.AddHostedService<SessionWatchdogService>();
        services.AddHostedService<PlaybackLifecycleService>();

        services.AddScoped<TestRunService>();
        services.AddScoped<RunQueries>();
        services.AddScoped<ReportService>();
        return services;
    }
}

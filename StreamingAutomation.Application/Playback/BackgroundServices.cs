using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StreamingAutomation.Application.Common.Options;
using StreamingAutomation.Domain.Common;

namespace StreamingAutomation.Application.Playback;

/// <summary>Periodically flushes buffered session state to the database; performs a final flush on shutdown.</summary>
public sealed class PersistenceFlushService(
    PlaybackPersistence persistence,
    IOptions<PlaybackOptions> options,
    ILogger<PersistenceFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.FlushIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await persistence.FlushAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Flush failed ({Message}); data is retained for the next attempt", ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await persistence.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Final flush on shutdown failed");
        }
    }
}

/// <summary>Fails sessions whose heartbeat stopped (hung connection, deadlock, …) so a run can never hang forever.</summary>
public sealed class SessionWatchdogService(
    RunRegistry registry,
    IOptions<PlaybackOptions> options,
    TimeProvider clock,
    ILogger<SessionWatchdogService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stallTimeout = TimeSpan.FromSeconds(options.Value.StallTimeoutSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.WatchdogIntervalSeconds), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var cutoff = clock.GetUtcNow() - stallTimeout;
                foreach (var run in registry.Active)
                {
                    foreach (var s in run.Snapshots)
                    {
                        if (s.Status.IsActive() && s.LastHeartbeatAt < cutoff && run.TryCancelSession(s.Id, Playback.CancelReason.Stalled))
                        {
                            logger.LogWarning("Watchdog: session {SessionId} (#{Sequence}) stalled since {LastHeartbeat:O}", s.Id, s.SequenceNumber, s.LastHeartbeatAt);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
    }
}

/// <summary>
/// Start-up: marks work orphaned by a previous crash as Interrupted (crash recovery).
/// Shutdown: interrupts active runs gracefully so their final state is persisted.
/// </summary>
public sealed class PlaybackLifecycleService(
    IPlaybackStateWriter writer,
    IPlaybackOrchestrator orchestrator,
    TimeProvider clock,
    ILogger<PlaybackLifecycleService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var (runs, sessions) = await writer.RecoverInterruptedAsync(clock.GetUtcNow(), cancellationToken);
        if (runs + sessions > 0)
        {
            logger.LogWarning("Recovered from previous shutdown: marked {Runs} run(s) and {Sessions} session(s) as Interrupted", runs, sessions);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => orchestrator.ShutdownAsync(cancellationToken);
}

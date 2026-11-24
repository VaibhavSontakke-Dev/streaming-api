using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using StreamingAutomation.Application.Common.Options;
using StreamingAutomation.Application.Monitoring;

namespace StreamingAutomation.API.Hubs;

/// <summary>
/// Throttled push: once per <see cref="PlaybackOptions.BroadcastIntervalMs"/> it sends one overview message to all
/// clients and one delta per run group – regardless of how many session events happened (hundreds per second at
/// high playback speeds). Network and browser load therefore stay constant as concurrency grows.
/// </summary>
public sealed class MonitoringBroadcaster(
    IHubContext<MonitoringHub, IMonitoringClient> hub,
    LiveMonitor monitor,
    IOptions<PlaybackOptions> options,
    ILogger<MonitoringBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.BroadcastIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var tick = monitor.NextTick();
                    if (!tick.HasChanges)
                    {
                        continue;
                    }

                    await hub.Clients.All.OverviewUpdated(tick.Overview);
                    foreach (var update in tick.RunUpdates)
                    {
                        await hub.Clients.Group(MonitoringHub.RunGroup(update.RunId)).RunUpdated(update);
                    }

                    foreach (var finished in tick.FinishedRuns)
                    {
                        await hub.Clients.All.RunFinished(finished);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Broadcast tick failed; continuing");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
    }
}

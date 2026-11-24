using Microsoft.AspNetCore.SignalR;
using StreamingAutomation.Application.Monitoring;

namespace StreamingAutomation.API.Hubs;

/// <summary>Server → client messages (strongly typed, so method names cannot drift).</summary>
public interface IMonitoringClient
{
    Task OverviewUpdated(OverviewUpdate update);

    Task RunUpdated(RunUpdate update);

    Task RunFinished(RunFinishedNotice notice);
}

/// <summary>
/// Real-time monitoring hub at <c>/hubs/monitoring</c>. Every client receives <c>OverviewUpdated</c>;
/// clients that call <see cref="SubscribeToRun"/> also receive per-run session deltas (<c>RunUpdated</c>).
/// </summary>
public sealed class MonitoringHub(LiveMonitor monitor) : Hub<IMonitoringClient>
{
    public const string Path = "/hubs/monitoring";

    public static string RunGroup(Guid runId) => $"run:{runId}";

    /// <summary>Joins the run's group and returns its full current state (null if the run is not executing).</summary>
    public async Task<RunLiveState?> SubscribeToRun(Guid runId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, RunGroup(runId));
        return monitor.GetRunState(runId);
    }

    public Task UnsubscribeFromRun(Guid runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, RunGroup(runId));

    public OverviewUpdate GetOverview() => monitor.GetOverview();
}

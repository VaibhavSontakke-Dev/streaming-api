using StreamingAutomation.Application.Playback;

namespace StreamingAutomation.Application.Monitoring;

/// <summary>Live summary of one executing run.</summary>
public sealed record RunLiveSummary(Guid RunId, string Name, double PlaybackSpeed, DateTimeOffset StartedAt, RunStatistics Stats);

/// <summary>Pushed to every dashboard each tick: all executing runs and their counters.</summary>
public sealed record OverviewUpdate(IReadOnlyList<RunLiveSummary> ActiveRuns, int TotalActiveSessions, int TotalPendingSessions, DateTimeOffset Timestamp);

/// <summary>Pushed to subscribers of a run: counters plus only the sessions that changed since the previous tick.</summary>
public sealed record RunUpdate(Guid RunId, RunStatistics Stats, IReadOnlyList<SessionSnapshot> ChangedSessions, DateTimeOffset Timestamp);

/// <summary>Full state returned when a client subscribes to a run (then kept current by <see cref="RunUpdate"/> deltas).</summary>
public sealed record RunLiveState(Guid RunId, RunStatistics Stats, IReadOnlyList<SessionSnapshot> Sessions, DateTimeOffset Timestamp);

/// <summary>Sent when a run leaves the engine (completed / cancelled / interrupted); clients re-fetch it over REST.</summary>
public sealed record RunFinishedNotice(Guid RunId, DateTimeOffset Timestamp);

/// <summary>Everything the broadcaster needs to send for one tick.</summary>
public sealed record MonitoringTick(OverviewUpdate Overview, IReadOnlyList<RunUpdate> RunUpdates, IReadOnlyList<RunFinishedNotice> FinishedRuns, bool HasChanges);

/// <summary>
/// Builds dashboard payloads from the engine's in-memory snapshots. <see cref="NextTick"/> is called by a single
/// broadcaster loop and diffs snapshots by reference (snapshots are immutable, so a new reference = a change).
/// This coalesces any number of session events into at most one message per run per tick.
/// </summary>
public sealed class LiveMonitor(RunRegistry registry, TimeProvider clock)
{
    private readonly Dictionary<Guid, Dictionary<Guid, SessionSnapshot>> _lastSent = [];
    private bool _hadActiveRuns;

    public OverviewUpdate GetOverview()
    {
        var runs = registry.Active
            .Select(r => new RunLiveSummary(r.RunId, r.Name, r.PlaybackSpeed, r.StartedAt, r.GetStatistics()))
            .OrderBy(r => r.StartedAt)
            .ToList();
        return new OverviewUpdate(runs, runs.Sum(r => r.Stats.Active), runs.Sum(r => r.Stats.Pending), clock.GetUtcNow());
    }

    public RunLiveState? GetRunState(Guid runId) =>
        registry.TryGet(runId, out var run)
            ? new RunLiveState(runId, run.GetStatistics(), run.Snapshots.OrderBy(s => s.SequenceNumber).ToList(), clock.GetUtcNow())
            : null;

    public MonitoringTick NextTick()
    {
        var now = clock.GetUtcNow();
        var overview = GetOverview();
        var updates = new List<RunUpdate>();
        var activeIds = new HashSet<Guid>();

        foreach (var run in registry.Active)
        {
            activeIds.Add(run.RunId);
            updates.Add(new RunUpdate(run.RunId, run.GetStatistics(), CollectChanges(run), now));
        }

        // Runs that finished since the last tick: send their final changes, then the finish notice.
        var finished = new List<RunFinishedNotice>();
        while (registry.TryDequeueFinished(out var run))
        {
            updates.Add(new RunUpdate(run.RunId, run.GetStatistics(), CollectChanges(run), now));
            finished.Add(new RunFinishedNotice(run.RunId, now));
            _lastSent.Remove(run.RunId);
        }

        var hasChanges = activeIds.Count > 0 || _hadActiveRuns || finished.Count > 0;
        _hadActiveRuns = activeIds.Count > 0;

        return new MonitoringTick(overview, updates, finished, hasChanges);
    }

    private List<SessionSnapshot> CollectChanges(RunContext run)
    {
        if (!_lastSent.TryGetValue(run.RunId, out var sent))
        {
            sent = [];
            _lastSent[run.RunId] = sent;
        }

        var changed = new List<SessionSnapshot>();
        foreach (var snapshot in run.Snapshots)
        {
            if (!sent.TryGetValue(snapshot.Id, out var previous) || !ReferenceEquals(previous, snapshot))
            {
                changed.Add(snapshot);
                sent[snapshot.Id] = snapshot;
            }
        }

        changed.Sort((a, b) => a.SequenceNumber.CompareTo(b.SequenceNumber));
        return changed;
    }
}

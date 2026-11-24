using System.Collections.Concurrent;

namespace StreamingAutomation.Application.Playback;

/// <summary>Registry of runs currently executing in this process.</summary>
public sealed class RunRegistry
{
    /// <summary>Upper bound on finished runs waiting for the live monitor (protects memory if nobody consumes them).</summary>
    private const int MaxFinishedBacklog = 100;

    private readonly ConcurrentDictionary<Guid, RunContext> _runs = new();
    private readonly ConcurrentQueue<RunContext> _finished = new();

    public ICollection<RunContext> Active => _runs.Values;

    public bool TryGet(Guid runId, out RunContext context) => _runs.TryGetValue(runId, out context!);

    public bool Register(RunContext context) => _runs.TryAdd(context.RunId, context);

    /// <summary>
    /// Removes a run that has finished. Its context is kept briefly so the live monitor can publish the
    /// last session changes (which happened after its previous tick) before announcing the run as finished.
    /// </summary>
    public bool Remove(Guid runId)
    {
        if (!_runs.TryRemove(runId, out var context))
        {
            return false;
        }

        _finished.Enqueue(context);
        while (_finished.Count > MaxFinishedBacklog && _finished.TryDequeue(out _))
        {
        }

        return true;
    }

    public bool TryDequeueFinished(out RunContext context) => _finished.TryDequeue(out context!);
}

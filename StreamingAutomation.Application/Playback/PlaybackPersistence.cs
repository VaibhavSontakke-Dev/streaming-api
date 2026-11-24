using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StreamingAutomation.Application.Common.Interfaces;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Application.Playback;

/// <summary>
/// Write-behind persistence. Workers never touch the database: they publish snapshots to their
/// <see cref="RunContext"/> (marking them dirty) and enqueue audit events here. A periodic flush coalesces
/// all changes since the last flush into one batch. A single lock makes this the only DB writer of session
/// state, so a stale snapshot can never overwrite a newer one.
/// </summary>
public sealed class PlaybackPersistence(
    IPlaybackStateWriter writer,
    RunRegistry registry,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<PlaybackPersistence> logger) : IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentQueue<SessionEvent> _events = new();
    private long _flushCount;
    private long _sessionRowsWritten;

    public long FlushCount => Interlocked.Read(ref _flushCount);

    public long SessionRowsWritten => Interlocked.Read(ref _sessionRowsWritten);

    public void Enqueue(SessionEvent sessionEvent) => _events.Enqueue(sessionEvent);

    /// <summary>Flushes all dirty sessions of all active runs.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await FlushCoreAsync(registry.Active.ToList(), allSessions: false, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Writes the complete final state of a run, applies its terminal status through the domain model and
    /// removes it from the registry – atomically with respect to periodic flushes.
    /// </summary>
    public async Task FinalizeRunAsync(RunContext run, RunStatus finalStatus, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await FlushCoreAsync([run], allSessions: true, cancellationToken);

            var stats = run.GetStatistics();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var entity = await db.TestRuns.SingleAsync(r => r.Id == run.RunId, cancellationToken);
            var now = clock.GetUtcNow();

            entity.UpdateStatistics(stats.Started, stats.Completed, stats.Failed, stats.Cancelled, stats.Interrupted, stats.PeakConcurrency, stats.BytesReceived);
            switch (finalStatus)
            {
                case RunStatus.Completed: entity.Complete(now); break;
                case RunStatus.Cancelled: entity.Cancel(now); break;
                case RunStatus.Interrupted: entity.Interrupt(now); break;
                default: throw new ArgumentOutOfRangeException(nameof(finalStatus), finalStatus, "Not a terminal run status.");
            }

            await db.SaveChangesAsync(cancellationToken);
            registry.Remove(run.RunId);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose() => _writeLock.Dispose();

    private async Task FlushCoreAsync(List<RunContext> runs, bool allSessions, CancellationToken cancellationToken)
    {
        var snapshots = new List<SessionSnapshot>();
        var runStats = new List<RunStatistics>(runs.Count);
        foreach (var run in runs)
        {
            var dirtyIds = run.DrainDirty().ToList();
            if (allSessions)
            {
                snapshots.AddRange(run.Snapshots);
            }
            else
            {
                foreach (var id in dirtyIds)
                {
                    if (run.TryGetSnapshot(id, out var snapshot))
                    {
                        snapshots.Add(snapshot);
                    }
                }
            }

            runStats.Add(run.GetStatistics());
        }

        var events = new List<SessionEvent>();
        while (_events.TryDequeue(out var e))
        {
            events.Add(e);
        }

        if (snapshots.Count == 0 && events.Count == 0 && runStats.Count == 0)
        {
            return;
        }

        try
        {
            await writer.WriteAsync(snapshots, events, runStats, cancellationToken);
            Interlocked.Increment(ref _flushCount);
            Interlocked.Add(ref _sessionRowsWritten, snapshots.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Database hiccup: keep everything for the next flush instead of losing it.
            logger.LogError(ex, "Persisting {SessionCount} sessions / {EventCount} events failed; will retry on next flush", snapshots.Count, events.Count);
            foreach (var run in runs)
            {
                foreach (var s in snapshots.Where(s => s.RunId == run.RunId))
                {
                    run.MarkDirty(s.Id);
                }
            }

            foreach (var e in events)
            {
                _events.Enqueue(e);
            }

            throw;
        }
    }
}

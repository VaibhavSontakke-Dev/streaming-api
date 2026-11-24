using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StreamingAutomation.Application.Common.Interfaces;
using StreamingAutomation.Application.Common.Options;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;
using StreamingAutomation.Domain.Exceptions;

namespace StreamingAutomation.Application.Playback;

public interface IPlaybackOrchestrator
{
    /// <summary>Marks the run Running and starts executing its sessions in the background.</summary>
    Task StartRunAsync(Guid runId, CancellationToken cancellationToken);

    bool CancelRun(Guid runId);

    bool CancelSession(Guid runId, Guid sessionId);

    /// <summary>Interrupts all runs and waits for them to finish persisting (host shutdown).</summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Concurrency model: one async task per session (I/O-bound, so hundreds are cheap), gated by a per-run
/// <see cref="SemaphoreSlim"/> (the run's MaxConcurrency) and a host-wide semaphore (MaxGlobalConcurrency).
/// Sessions are launched with an even ramp-up spread. Each session is owned by exactly one task at a time.
/// </summary>
public sealed class PlaybackOrchestrator : IPlaybackOrchestrator, IDisposable
{
    private readonly RunRegistry _registry;
    private readonly PlaybackPersistence _persistence;
    private readonly SessionWorker _worker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<PlaybackOrchestrator> _logger;
    private readonly CancellationToken _hostStopping;
    private readonly SemaphoreSlim _globalLimiter;

    public PlaybackOrchestrator(
        RunRegistry registry,
        PlaybackPersistence persistence,
        SessionWorker worker,
        IServiceScopeFactory scopeFactory,
        IOptions<PlaybackOptions> options,
        IHostApplicationLifetime lifetime,
        TimeProvider clock,
        ILogger<PlaybackOrchestrator> logger)
    {
        _registry = registry;
        _persistence = persistence;
        _worker = worker;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
        _hostStopping = lifetime.ApplicationStopping;
        _globalLimiter = new SemaphoreSlim(options.Value.MaxGlobalConcurrency, options.Value.MaxGlobalConcurrency);
    }

    public async Task StartRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var run = await db.TestRuns.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Test run {runId} not found.");

        // Tracked so the orchestrator owns these entity instances for the lifetime of the run.
        var sessions = await db.PlaybackSessions
            .Include(s => s.MediaItem)
            .Where(s => s.TestRunId == runId)
            .OrderBy(s => s.SequenceNumber)
            .ToListAsync(cancellationToken);

        run.Start(_clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var context = new RunContext(run, _hostStopping);
        var assignments = sessions
            .Select(s => new SessionAssignment(s, s.MediaItem?.ExternalId ?? s.MediaItemId.ToString(), s.MediaItem?.Title ?? "Unknown media"))
            .ToList();
        foreach (var a in assignments)
        {
            context.Seed(SessionSnapshot.From(a.Session, a.MediaTitle));
        }

        if (!_registry.Register(context))
        {
            context.Dispose();
            throw new DomainException($"Run {runId} is already executing.");
        }

        _logger.LogInformation(
            "Run {RunId} '{Name}' starting: {Sessions} sessions, concurrency {Concurrency}, ramp-up {RampUp}s, speed x{Speed}, failure rate {FailureRate:P0}",
            run.Id, run.Name, run.RequestedSessions, run.MaxConcurrency, run.RampUpSeconds, run.PlaybackSpeed, run.SimulatedFailureRate);

        // The run outlives the HTTP request that started it: don't let it inherit the request's
        // execution context (log scopes, Activity/trace id, AsyncLocals).
        using (ExecutionContext.SuppressFlow())
        {
            context.Execution = Task.Run(() => ExecuteRunAsync(context, assignments), CancellationToken.None);
        }
    }

    public bool CancelRun(Guid runId)
    {
        if (!_registry.TryGet(runId, out var context))
        {
            return false;
        }

        context.CancelRun(CancelReason.User);
        return true;
    }

    public bool CancelSession(Guid runId, Guid sessionId) =>
        _registry.TryGet(runId, out var context) && context.TryCancelSession(sessionId, CancelReason.User);

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        var runs = _registry.Active.ToList();
        if (runs.Count == 0)
        {
            return;
        }

        _logger.LogWarning("Shutdown: interrupting {Count} active run(s)", runs.Count);
        foreach (var run in runs)
        {
            run.CancelRun(CancelReason.Shutdown);
        }

        await Task.WhenAll(runs.Select(r => r.Execution)).WaitAsync(cancellationToken);
    }

    public void Dispose() => _globalLimiter.Dispose();

    private async Task ExecuteRunAsync(RunContext run, List<SessionAssignment> assignments)
    {
        using var runLimiter = new SemaphoreSlim(run.MaxConcurrency, run.MaxConcurrency);
        var rampDelay = run.RampUpSeconds > 0 && assignments.Count > 1
            ? TimeSpan.FromSeconds(run.RampUpSeconds / (double)(assignments.Count - 1))
            : TimeSpan.Zero;
        var tasks = new List<Task>(assignments.Count);
        var launched = new HashSet<Guid>();
        var rampStarted = _clock.GetTimestamp();

        try
        {
            for (var i = 0; i < assignments.Count; i++)
            {
                var assignment = assignments[i];
                if (run.IsCancelledBeforeStart(assignment.Session.Id))
                {
                    continue; // individually cancelled while still pending – finalized below
                }

                // Schedule against an absolute target (start + i × interval) instead of sleeping a fixed interval:
                // OS timers are coarse (~15.6 ms on Windows), so per-session sleeps of a few ms would silently
                // stretch the ramp-up. When the timer fires late, the overdue sessions start back-to-back.
                if (rampDelay > TimeSpan.Zero)
                {
                    var wait = (rampDelay * i) - _clock.GetElapsedTime(rampStarted);
                    if (wait > TimeSpan.FromMilliseconds(1))
                    {
                        await Task.Delay(wait, _clock, run.RunToken);
                    }
                }

                await runLimiter.WaitAsync(run.RunToken);
                try
                {
                    await _globalLimiter.WaitAsync(run.RunToken);
                }
                catch
                {
                    runLimiter.Release();
                    throw;
                }

                var handle = run.CreateHandle(assignment.Session.Id);
                if (run.IsCancelledBeforeStart(assignment.Session.Id))
                {
                    handle.Cancel(CancelReason.User); // cancel raced with launch
                }

                launched.Add(assignment.Session.Id);
                tasks.Add(RunSessionAsync(run, assignment, handle, runLimiter));
            }
        }
        catch (OperationCanceledException) when (run.RunToken.IsCancellationRequested)
        {
            _logger.LogInformation("Run {RunId} cancelled ({Reason}) after launching {Launched} sessions", run.RunId, run.RunCancelReason, launched.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId} launcher crashed; cancelling remaining sessions", run.RunId);
            run.CancelRun(CancelReason.User);
        }

        await Task.WhenAll(tasks);

        // Sessions that never started: cancelled (user) or interrupted (shutdown).
        var now = _clock.GetUtcNow();
        foreach (var assignment in assignments.Where(a => !launched.Contains(a.Session.Id)))
        {
            var from = assignment.Session.Status;
            if (run.RunCancelReason == CancelReason.Shutdown)
            {
                assignment.Session.Interrupt(now);
            }
            else
            {
                assignment.Session.Cancel(now);
            }

            run.Publish(SessionSnapshot.From(assignment.Session, assignment.MediaTitle));
            _persistence.Enqueue(new SessionEvent(assignment.Session.Id, run.RunId, from, assignment.Session.Status, "Not started", now));
        }

        var finalStatus = run.RunCancelReason switch
        {
            CancelReason.Shutdown => RunStatus.Interrupted,
            CancelReason.User => RunStatus.Cancelled,
            _ => RunStatus.Completed,
        };

        try
        {
            await _persistence.FinalizeRunAsync(run, finalStatus, CancellationToken.None);
            var stats = run.GetStatistics();
            _logger.LogInformation(
                "Run {RunId} {Status}: started {Started}, completed {Completed}, failed {Failed}, cancelled {Cancelled}, interrupted {Interrupted}, peak concurrency {Peak}",
                run.RunId, finalStatus, stats.Started, stats.Completed, stats.Failed, stats.Cancelled, stats.Interrupted, stats.PeakConcurrency);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize run {RunId}; it will be marked Interrupted on next start-up", run.RunId);
            _registry.Remove(run.RunId);
        }
        finally
        {
            run.Dispose();
        }
    }

    private async Task RunSessionAsync(RunContext run, SessionAssignment assignment, SessionHandle handle, SemaphoreSlim runLimiter)
    {
        try
        {
            await Task.Yield(); // let the launcher continue immediately
            await _worker.RunAsync(run, assignment, handle);
        }
        finally
        {
            run.ReleaseHandle(assignment.Session.Id);
            _globalLimiter.Release();
            runLimiter.Release();
        }
    }
}

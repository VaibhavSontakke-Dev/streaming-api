using System.Collections.Concurrent;
using StreamingAutomation.Domain.Common;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Application.Playback;

/// <summary>Why a session's token was cancelled – decides the terminal status (Cancelled / Failed / Interrupted).</summary>
public enum CancelReason
{
    None = 0,
    User = 1,
    Stalled = 2,
    Shutdown = 3,
}

/// <summary>Cancellation handle for one running session.</summary>
public sealed class SessionHandle : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private int _reason;

    public SessionHandle(CancellationToken runToken) => _cts = CancellationTokenSource.CreateLinkedTokenSource(runToken);

    public CancellationToken Token => _cts.Token;

    public CancelReason Reason => (CancelReason)Volatile.Read(ref _reason);

    public void Cancel(CancelReason reason)
    {
        Interlocked.CompareExchange(ref _reason, (int)reason, (int)CancelReason.None);
        _cts.Cancel();
    }

    public void Dispose() => _cts.Dispose();
}

/// <summary>
/// In-memory "hot" state of an executing run: latest session snapshots, cancellation handles, dirty set for
/// the persistence flusher and lock-free counters. Shared by the orchestrator, workers, watchdog,
/// flusher and live dashboard broadcaster.
/// </summary>
public sealed class RunContext : IDisposable
{
    private readonly CancellationTokenSource _runCts;
    private readonly ConcurrentDictionary<Guid, SessionSnapshot> _sessions = new();
    private readonly ConcurrentDictionary<Guid, SessionHandle> _handles = new();
    private readonly ConcurrentDictionary<Guid, byte> _dirty = new();
    private readonly ConcurrentDictionary<Guid, byte> _cancelBeforeStart = new();
    private readonly CancellationTokenRegistration _hostStoppingRegistration;
    private int _runCancelReason;

    private int _started;
    private int _completed;
    private int _failed;
    private int _cancelled;
    private int _interrupted;
    private int _terminalBeforeStart;
    private int _active;
    private int _peak;
    private long _bytes;

    public RunContext(TestRun run, CancellationToken hostStopping)
    {
        RunId = run.Id;
        Name = run.Name;
        RequestedSessions = run.RequestedSessions;
        MaxConcurrency = run.MaxConcurrency;
        RampUpSeconds = run.RampUpSeconds;
        PlaybackSpeed = run.PlaybackSpeed;
        SimulatedFailureRate = run.SimulatedFailureRate;
        StartedAt = run.StartedAt ?? DateTimeOffset.UtcNow;
        _runCts = new CancellationTokenSource();
        _hostStoppingRegistration = hostStopping.Register(() => CancelRun(CancelReason.Shutdown));
    }

    public Guid RunId { get; }

    public string Name { get; }

    public int RequestedSessions { get; }

    public int MaxConcurrency { get; }

    public int RampUpSeconds { get; }

    public double PlaybackSpeed { get; }

    public double SimulatedFailureRate { get; }

    public DateTimeOffset StartedAt { get; }

    public CancellationToken RunToken => _runCts.Token;

    public CancelReason RunCancelReason => (CancelReason)Volatile.Read(ref _runCancelReason);

    /// <summary>The background task executing this run (awaited on shutdown).</summary>
    public Task Execution { get; set; } = Task.CompletedTask;

    public ICollection<SessionSnapshot> Snapshots => _sessions.Values;

    public bool TryGetSnapshot(Guid sessionId, out SessionSnapshot snapshot) => _sessions.TryGetValue(sessionId, out snapshot!);

    /// <summary>Seeds the initial (Pending) snapshot without marking it dirty.</summary>
    public void Seed(SessionSnapshot snapshot) => _sessions[snapshot.Id] = snapshot;

    /// <summary>
    /// Publishes a new snapshot. Must be called only by the session's current owner (single writer per session),
    /// which makes the read-previous / write-new sequence race-free.
    /// </summary>
    public void Publish(SessionSnapshot snapshot)
    {
        _sessions.TryGetValue(snapshot.Id, out var previous);
        _sessions[snapshot.Id] = snapshot;
        _dirty[snapshot.Id] = 0;

        if (previous is not null && snapshot.BytesReceived != previous.BytesReceived)
        {
            Interlocked.Add(ref _bytes, snapshot.BytesReceived - previous.BytesReceived);
        }

        var from = previous?.Status ?? SessionStatus.Pending;
        if (from != snapshot.Status)
        {
            OnTransition(from, snapshot.Status);
        }
    }

    public IEnumerable<Guid> DrainDirty()
    {
        foreach (var id in _dirty.Keys)
        {
            if (_dirty.TryRemove(id, out _))
            {
                yield return id;
            }
        }
    }

    public void MarkDirty(Guid sessionId) => _dirty[sessionId] = 0;

    public SessionHandle CreateHandle(Guid sessionId)
    {
        var handle = new SessionHandle(RunToken);
        _handles[sessionId] = handle;
        return handle;
    }

    public void ReleaseHandle(Guid sessionId)
    {
        if (_handles.TryRemove(sessionId, out var handle))
        {
            handle.Dispose();
        }
    }

    /// <summary>Requests cancellation of one session. Returns false if the session is unknown or already finished.</summary>
    public bool TryCancelSession(Guid sessionId, CancelReason reason)
    {
        if (!_sessions.TryGetValue(sessionId, out var snapshot) || snapshot.Status.IsTerminal())
        {
            return false;
        }

        if (_handles.TryGetValue(sessionId, out var handle))
        {
            handle.Cancel(reason);
        }
        else
        {
            _cancelBeforeStart[sessionId] = 0; // picked up by the orchestrator before it starts the session
        }

        return true;
    }

    public bool IsCancelledBeforeStart(Guid sessionId) => _cancelBeforeStart.ContainsKey(sessionId);

    public void CancelRun(CancelReason reason)
    {
        Interlocked.CompareExchange(ref _runCancelReason, (int)reason, (int)CancelReason.None);
        try
        {
            _runCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // run already finished
        }
    }

    public RunStatistics GetStatistics()
    {
        var started = Volatile.Read(ref _started);
        return new RunStatistics(
            RunId,
            RequestedSessions,
            Pending: Math.Max(0, RequestedSessions - started - Volatile.Read(ref _terminalBeforeStart)),
            Active: Volatile.Read(ref _active),
            Started: started,
            Completed: Volatile.Read(ref _completed),
            Failed: Volatile.Read(ref _failed),
            Cancelled: Volatile.Read(ref _cancelled),
            Interrupted: Volatile.Read(ref _interrupted),
            PeakConcurrency: Volatile.Read(ref _peak),
            BytesReceived: Interlocked.Read(ref _bytes));
    }

    public void Dispose()
    {
        foreach (var handle in _handles.Values)
        {
            handle.Dispose();
        }

        _hostStoppingRegistration.Dispose();
        _runCts.Dispose();
    }

    private void OnTransition(SessionStatus from, SessionStatus to)
    {
        if (to == SessionStatus.Starting)
        {
            Interlocked.Increment(ref _started);
            UpdatePeak(Interlocked.Increment(ref _active));
        }

        if (!to.IsTerminal())
        {
            return;
        }

        if (from.IsActive())
        {
            Interlocked.Decrement(ref _active);
        }
        else if (from == SessionStatus.Pending)
        {
            Interlocked.Increment(ref _terminalBeforeStart);
        }

        switch (to)
        {
            case SessionStatus.Completed: Interlocked.Increment(ref _completed); break;
            case SessionStatus.Failed: Interlocked.Increment(ref _failed); break;
            case SessionStatus.Cancelled: Interlocked.Increment(ref _cancelled); break;
            case SessionStatus.Interrupted: Interlocked.Increment(ref _interrupted); break;
        }
    }

    private void UpdatePeak(int candidate)
    {
        int current;
        while (candidate > (current = Volatile.Read(ref _peak)))
        {
            if (Interlocked.CompareExchange(ref _peak, candidate, current) == current)
            {
                return;
            }
        }
    }
}

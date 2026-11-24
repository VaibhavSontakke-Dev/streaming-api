using StreamingAutomation.Domain.Enums;
using StreamingAutomation.Domain.Exceptions;

namespace StreamingAutomation.Domain.Entities;

/// <summary>
/// A test run: one request to start N simulated playback sessions with a concurrency limit.
/// Aggregate counters are maintained by the orchestration engine and persisted for reporting.
/// </summary>
public class TestRun
{
    private TestRun() { } // EF Core

    public TestRun(
        Guid id,
        string name,
        string platform,
        int requestedSessions,
        int maxConcurrency,
        int rampUpSeconds,
        double playbackSpeed,
        double simulatedFailureRate,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestedSessions, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxConcurrency, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(rampUpSeconds);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(playbackSpeed, 0);
        if (simulatedFailureRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(simulatedFailureRate), "Must be between 0 and 1.");
        }

        Id = id;
        Name = name;
        Platform = platform;
        RequestedSessions = requestedSessions;
        MaxConcurrency = maxConcurrency;
        RampUpSeconds = rampUpSeconds;
        PlaybackSpeed = playbackSpeed;
        SimulatedFailureRate = simulatedFailureRate;
        CreatedAt = createdAt;
        Status = RunStatus.Pending;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Platform { get; private set; } = string.Empty;

    public RunStatus Status { get; private set; }

    // --- configuration -------------------------------------------------
    public int RequestedSessions { get; private set; }

    public int MaxConcurrency { get; private set; }

    public int RampUpSeconds { get; private set; }

    /// <summary>Time-scale factor: 1.0 = real time, 10.0 = ten times faster than real time.</summary>
    public double PlaybackSpeed { get; private set; }

    /// <summary>Probability (0–1) that the test media server injects a fault for this run.</summary>
    public double SimulatedFailureRate { get; private set; }

    // --- aggregate results ----------------------------------------------
    public int StartedCount { get; private set; }

    public int CompletedCount { get; private set; }

    public int FailedCount { get; private set; }

    public int CancelledCount { get; private set; }

    public int InterruptedCount { get; private set; }

    public int PeakConcurrency { get; private set; }

    public long TotalBytesReceived { get; private set; }

    // --- timestamps -------------------------------------------------------
    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public ICollection<PlaybackSession> Sessions { get; private set; } = [];

    public bool IsFinished => Status is RunStatus.Completed or RunStatus.Cancelled or RunStatus.Interrupted;

    public void Start(DateTimeOffset now)
    {
        EnsureStatus(RunStatus.Running, RunStatus.Pending);
        Status = RunStatus.Running;
        StartedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureStatus(RunStatus.Completed, RunStatus.Running);
        Status = RunStatus.Completed;
        EndedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureStatus(RunStatus.Cancelled, RunStatus.Pending, RunStatus.Running);
        Status = RunStatus.Cancelled;
        EndedAt = now;
    }

    public void Interrupt(DateTimeOffset now)
    {
        EnsureStatus(RunStatus.Interrupted, RunStatus.Pending, RunStatus.Running);
        Status = RunStatus.Interrupted;
        EndedAt = now;
    }

    /// <summary>Overwrites aggregate counters with the latest values computed by the engine.</summary>
    public void UpdateStatistics(int started, int completed, int failed, int cancelled, int interrupted, int peakConcurrency, long totalBytes)
    {
        StartedCount = started;
        CompletedCount = completed;
        FailedCount = failed;
        CancelledCount = cancelled;
        InterruptedCount = interrupted;
        PeakConcurrency = Math.Max(PeakConcurrency, peakConcurrency);
        TotalBytesReceived = totalBytes;
    }

    private void EnsureStatus(RunStatus target, params RunStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
        {
            throw new InvalidStateTransitionException(nameof(TestRun), Status, target);
        }
    }
}

using StreamingAutomation.Domain.Common;
using StreamingAutomation.Domain.Enums;
using StreamingAutomation.Domain.Exceptions;

namespace StreamingAutomation.Domain.Entities;

/// <summary>
/// A single simulated playback session. All status changes go through <see cref="SessionStateMachine"/>.
/// The entity is mutated by exactly one worker task at a time (single-writer), so it needs no locking.
/// </summary>
public class PlaybackSession
{
    private PlaybackSession() { } // EF Core

    public PlaybackSession(Guid id, Guid testRunId, Guid mediaItemId, int sequenceNumber, int durationSeconds, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(durationSeconds, 0);

        Id = id;
        TestRunId = testRunId;
        MediaItemId = mediaItemId;
        SequenceNumber = sequenceNumber;
        DurationSeconds = durationSeconds;
        CreatedAt = createdAt;
        Status = SessionStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid TestRunId { get; private set; }

    public Guid MediaItemId { get; private set; }

    /// <summary>1-based ordinal of the session inside its run (for readable dashboards).</summary>
    public int SequenceNumber { get; private set; }

    public SessionStatus Status { get; private set; }

    public double PositionSeconds { get; private set; }

    public int DurationSeconds { get; private set; }

    public long BytesReceived { get; private set; }

    public int SegmentsReceived { get; private set; }

    public int RetryCount { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? PlaybackStartedAt { get; private set; }

    public DateTimeOffset? LastHeartbeatAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public MediaItem? MediaItem { get; private set; }

    /// <summary>Progress 0–100, derived from position and duration.</summary>
    public double ProgressPercent => DurationSeconds == 0 ? 0 : Math.Min(100, PositionSeconds / DurationSeconds * 100);

    public void Start(DateTimeOffset now)
    {
        TransitionTo(SessionStatus.Starting);
        StartedAt = now;
        LastHeartbeatAt = now;
    }

    public void BeginPlayback(DateTimeOffset now)
    {
        TransitionTo(SessionStatus.Playing);
        PlaybackStartedAt ??= now;
        LastHeartbeatAt = now;
    }

    public void EnterBuffering(DateTimeOffset now)
    {
        TransitionTo(SessionStatus.Buffering);
        LastHeartbeatAt = now;
    }

    public void ResumePlayback(DateTimeOffset now)
    {
        TransitionTo(SessionStatus.Playing);
        LastHeartbeatAt = now;
    }

    /// <summary>Records a successfully received media segment.</summary>
    public void RecordSegment(double positionSeconds, long bytes, DateTimeOffset now)
    {
        if (Status is not (SessionStatus.Playing or SessionStatus.Buffering))
        {
            throw new DomainException($"Cannot record progress while session is {Status}.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        PositionSeconds = Math.Clamp(positionSeconds, PositionSeconds, DurationSeconds);
        BytesReceived += bytes;
        SegmentsReceived++;
        LastHeartbeatAt = now;
    }

    public void Heartbeat(DateTimeOffset now) => LastHeartbeatAt = now;

    public void RecordRetry() => RetryCount++;

    public void Complete(DateTimeOffset now)
    {
        TransitionTo(SessionStatus.Completed);
        PositionSeconds = DurationSeconds;
        EndedAt = now;
        LastHeartbeatAt = now;
    }

    public void Fail(string errorCode, string errorMessage, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        TransitionTo(SessionStatus.Failed);
        ErrorCode = errorCode;
        ErrorMessage = Truncate(errorMessage, 1000);
        EndedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        TransitionTo(SessionStatus.Cancelled);
        EndedAt = now;
    }

    public void Interrupt(DateTimeOffset now)
    {
        TransitionTo(SessionStatus.Interrupted);
        EndedAt = now;
    }

    private void TransitionTo(SessionStatus target)
    {
        if (!SessionStateMachine.CanTransition(Status, target))
        {
            throw new InvalidStateTransitionException(nameof(PlaybackSession), Status, target);
        }

        Status = target;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}

using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Application.Playback;

/// <summary>
/// Immutable point-in-time copy of a session. Workers publish a new snapshot after every change;
/// the persistence flusher, watchdog and live dashboard only ever read snapshots, never the mutable entity.
/// </summary>
public sealed record SessionSnapshot(
    Guid Id,
    Guid RunId,
    int SequenceNumber,
    Guid MediaItemId,
    string MediaTitle,
    SessionStatus Status,
    double PositionSeconds,
    int DurationSeconds,
    double ProgressPercent,
    long BytesReceived,
    int SegmentsReceived,
    int RetryCount,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? PlaybackStartedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? EndedAt)
{
    public static SessionSnapshot From(PlaybackSession s, string mediaTitle) => new(
        s.Id,
        s.TestRunId,
        s.SequenceNumber,
        s.MediaItemId,
        mediaTitle,
        s.Status,
        s.PositionSeconds,
        s.DurationSeconds,
        Math.Round(s.ProgressPercent, 2),
        s.BytesReceived,
        s.SegmentsReceived,
        s.RetryCount,
        s.ErrorCode,
        s.ErrorMessage,
        s.CreatedAt,
        s.StartedAt,
        s.PlaybackStartedAt,
        s.LastHeartbeatAt,
        s.EndedAt);
}

/// <summary>Aggregate counters for a run, computed lock-free from session transitions.</summary>
public sealed record RunStatistics(
    Guid RunId,
    int Requested,
    int Pending,
    int Active,
    int Started,
    int Completed,
    int Failed,
    int Cancelled,
    int Interrupted,
    int PeakConcurrency,
    long BytesReceived);

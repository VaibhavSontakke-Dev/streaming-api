using StreamingAutomation.Application.Playback;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Application.Runs;

/// <summary>A test run with configuration and (live or persisted) counters.</summary>
public sealed record RunDto(
    Guid Id,
    string Name,
    string Platform,
    RunStatus Status,
    bool IsLive,
    int RequestedSessions,
    int MaxConcurrency,
    int RampUpSeconds,
    double PlaybackSpeed,
    double SimulatedFailureRate,
    int Pending,
    int Active,
    int Started,
    int Completed,
    int Failed,
    int Cancelled,
    int Interrupted,
    int PeakConcurrency,
    long TotalBytesReceived,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    double? DurationSeconds)
{
    /// <summary>Builds the DTO; when the run is executing, <paramref name="live"/> counters (sub-second fresh) win.</summary>
    public static RunDto From(TestRun run, RunStatistics? live, DateTimeOffset now)
    {
        return new RunDto(
            run.Id,
            run.Name,
            run.Platform,
            run.Status,
            IsLive: live is not null,
            run.RequestedSessions,
            run.MaxConcurrency,
            run.RampUpSeconds,
            run.PlaybackSpeed,
            run.SimulatedFailureRate,
            Pending: live?.Pending ?? (run.Status == RunStatus.Pending ? run.RequestedSessions : 0),
            Active: live?.Active ?? 0,
            Started: live?.Started ?? run.StartedCount,
            Completed: live?.Completed ?? run.CompletedCount,
            Failed: live?.Failed ?? run.FailedCount,
            Cancelled: live?.Cancelled ?? run.CancelledCount,
            Interrupted: live?.Interrupted ?? run.InterruptedCount,
            PeakConcurrency: Math.Max(live?.PeakConcurrency ?? 0, run.PeakConcurrency),
            TotalBytesReceived: live?.BytesReceived ?? run.TotalBytesReceived,
            run.CreatedAt,
            run.StartedAt,
            run.EndedAt,
            run.StartedAt is { } s ? Math.Round(((run.EndedAt ?? now) - s).TotalSeconds, 1) : null);
    }
}

public sealed record SessionEventDto(long Id, SessionStatus? FromStatus, SessionStatus ToStatus, string? Message, DateTimeOffset OccurredAt);

public sealed record MediaItemDto(Guid Id, string Title, string Platform, string? ExternalId, int DurationSeconds);

/// <summary>Filters for listing runs.</summary>
public sealed record RunQuery(RunStatus? Status = null, int Page = 1, int PageSize = 20);

/// <summary>Filters for listing sessions of a run.</summary>
/// <param name="Status">Comma-separated statuses, or the groups "active" / "terminal".</param>
/// <param name="ErrorCode">Exact error code (e.g. STREAM_BROKEN).</param>
/// <param name="SortBy">sequence | status | progress | startedAt | endedAt | bytes | retries.</param>
public sealed record SessionQuery(
    string? Status = null,
    string? ErrorCode = null,
    string SortBy = "sequence",
    bool Descending = false,
    int Page = 1,
    int PageSize = 50);

using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Infrastructure.Persistence;

/// <summary>
/// Set-based writer: all dirty sessions are updated with a single <c>UPDATE … FROM unnest(arrays)</c>
/// statement (one round trip regardless of session count), events are batch-inserted by EF Core,
/// all inside one transaction.
/// </summary>
internal sealed class PlaybackStateWriter(IDbContextFactory<AppDbContext> contextFactory) : IPlaybackStateWriter
{
    private const string UpdateSessionsSql = """
        UPDATE playback_sessions AS s SET
            status              = u.status,
            position_seconds    = u.position_seconds,
            bytes_received      = u.bytes_received,
            segments_received   = u.segments_received,
            retry_count         = u.retry_count,
            error_code          = u.error_code,
            error_message       = u.error_message,
            started_at          = u.started_at,
            playback_started_at = u.playback_started_at,
            last_heartbeat_at   = u.last_heartbeat_at,
            ended_at            = u.ended_at
        FROM unnest(@ids, @statuses, @positions, @bytes, @segments, @retries, @error_codes, @error_messages,
                    @started_at, @playback_started_at, @last_heartbeat_at, @ended_at)
             AS u(id, status, position_seconds, bytes_received, segments_received, retry_count, error_code, error_message,
                  started_at, playback_started_at, last_heartbeat_at, ended_at)
        WHERE s.id = u.id
        """;

    private static readonly SessionStatus[] NonTerminalSessionStatuses =
        [SessionStatus.Pending, SessionStatus.Starting, SessionStatus.Playing, SessionStatus.Buffering];

    public async Task WriteAsync(
        IReadOnlyCollection<SessionSnapshot> sessions,
        IReadOnlyCollection<SessionEvent> events,
        IReadOnlyCollection<RunStatistics> runs,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            if (sessions.Count > 0)
            {
                await db.Database.ExecuteSqlRawAsync(UpdateSessionsSql, BuildParameters(sessions), ct);
            }

            foreach (var run in runs)
            {
                await db.TestRuns
                    .Where(r => r.Id == run.RunId)
                    .ExecuteUpdateAsync(
                        u => u
                            .SetProperty(r => r.StartedCount, run.Started)
                            .SetProperty(r => r.CompletedCount, run.Completed)
                            .SetProperty(r => r.FailedCount, run.Failed)
                            .SetProperty(r => r.CancelledCount, run.Cancelled)
                            .SetProperty(r => r.InterruptedCount, run.Interrupted)
                            .SetProperty(r => r.PeakConcurrency, run.PeakConcurrency)
                            .SetProperty(r => r.TotalBytesReceived, run.BytesReceived),
                        ct);
            }

            if (events.Count > 0)
            {
                db.SessionEvents.AddRange(events);
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        }, cancellationToken);
    }

    public async Task<(int Runs, int Sessions)> RecoverInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var sessions = await db.PlaybackSessions
            .Where(s => NonTerminalSessionStatuses.Contains(s.Status))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Interrupted).SetProperty(s => s.EndedAt, now), cancellationToken);

        var runs = await db.TestRuns
            .Where(r => r.Status == RunStatus.Pending || r.Status == RunStatus.Running)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, RunStatus.Interrupted).SetProperty(r => r.EndedAt, now), cancellationToken);

        return (runs, sessions);
    }

    private static NpgsqlParameter[] BuildParameters(IReadOnlyCollection<SessionSnapshot> sessions)
    {
        var list = sessions as IList<SessionSnapshot> ?? sessions.ToList();
        return
        [
            Array("ids", NpgsqlDbType.Uuid, list.Select(s => s.Id).ToArray()),
            Array("statuses", NpgsqlDbType.Text, list.Select(s => s.Status.ToString()).ToArray()),
            Array("positions", NpgsqlDbType.Double, list.Select(s => s.PositionSeconds).ToArray()),
            Array("bytes", NpgsqlDbType.Bigint, list.Select(s => s.BytesReceived).ToArray()),
            Array("segments", NpgsqlDbType.Integer, list.Select(s => s.SegmentsReceived).ToArray()),
            Array("retries", NpgsqlDbType.Integer, list.Select(s => s.RetryCount).ToArray()),
            Array("error_codes", NpgsqlDbType.Text, list.Select(s => s.ErrorCode).ToArray()),
            Array("error_messages", NpgsqlDbType.Text, list.Select(s => s.ErrorMessage).ToArray()),
            Array("started_at", NpgsqlDbType.TimestampTz, list.Select(s => Utc(s.StartedAt)).ToArray()),
            Array("playback_started_at", NpgsqlDbType.TimestampTz, list.Select(s => Utc(s.PlaybackStartedAt)).ToArray()),
            Array("last_heartbeat_at", NpgsqlDbType.TimestampTz, list.Select(s => Utc(s.LastHeartbeatAt)).ToArray()),
            Array("ended_at", NpgsqlDbType.TimestampTz, list.Select(s => Utc(s.EndedAt)).ToArray()),
        ];
    }

    private static NpgsqlParameter Array<T>(string name, NpgsqlDbType elementType, T[] values) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values };

    private static DateTime? Utc(DateTimeOffset? value) => value?.UtcDateTime;
}

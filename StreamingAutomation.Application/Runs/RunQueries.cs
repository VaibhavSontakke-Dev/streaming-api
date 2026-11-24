using Microsoft.EntityFrameworkCore;
using StreamingAutomation.Application.Common.Exceptions;
using StreamingAutomation.Application.Common.Interfaces;
using StreamingAutomation.Application.Common.Models;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Domain.Common;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Application.Runs;

/// <summary>
/// Read side. For runs that are executing right now, session data is served from the in-memory snapshots
/// (fresher than the database, which lags by at most one flush interval); finished runs come from PostgreSQL.
/// </summary>
public sealed class RunQueries(IApplicationDbContext db, RunRegistry registry, TimeProvider clock)
{
    public async Task<PagedResult<RunDto>> ListRunsAsync(RunQuery query, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(query.Page, query.PageSize);
        var runs = db.TestRuns.AsNoTracking();
        if (query.Status is { } status)
        {
            runs = runs.Where(r => r.Status == status);
        }

        var total = await runs.CountAsync(cancellationToken);
        var page = await runs
            .OrderByDescending(r => r.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.SafePageSize)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        return new PagedResult<RunDto>(page.Select(r => ToDto(r, now)).ToList(), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<RunDto> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await FindRunAsync(runId, cancellationToken);
        return ToDto(run, clock.GetUtcNow());
    }

    public async Task<PagedResult<SessionSnapshot>> ListSessionsAsync(Guid runId, SessionQuery query, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(query.Page, query.PageSize);
        var statuses = ParseStatuses(query.Status);

        if (registry.TryGet(runId, out var live))
        {
            var filtered = live.Snapshots.AsEnumerable();
            if (statuses is not null)
            {
                filtered = filtered.Where(s => statuses.Contains(s.Status));
            }

            if (!string.IsNullOrWhiteSpace(query.ErrorCode))
            {
                filtered = filtered.Where(s => string.Equals(s.ErrorCode, query.ErrorCode, StringComparison.OrdinalIgnoreCase));
            }

            var list = SortInMemory(filtered, query.SortBy, query.Descending).ToList();
            return new PagedResult<SessionSnapshot>(
                list.Skip(paging.Skip).Take(paging.SafePageSize).ToList(), paging.SafePage, paging.SafePageSize, list.Count);
        }

        await EnsureRunExistsAsync(runId, cancellationToken);

        var sessions = db.PlaybackSessions.AsNoTracking().Where(s => s.TestRunId == runId);
        if (statuses is not null)
        {
            sessions = sessions.Where(s => statuses.Contains(s.Status));
        }

        if (!string.IsNullOrWhiteSpace(query.ErrorCode))
        {
            var code = query.ErrorCode.Trim().ToUpperInvariant();
            sessions = sessions.Where(s => s.ErrorCode == code);
        }

        var total = await sessions.CountAsync(cancellationToken);
        var page = await SortInDatabase(sessions, query.SortBy, query.Descending)
            .Include(s => s.MediaItem)
            .Skip(paging.Skip)
            .Take(paging.SafePageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<SessionSnapshot>(
            page.Select(s => SessionSnapshot.From(s, s.MediaItem?.Title ?? string.Empty)).ToList(), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<SessionSnapshot> GetSessionAsync(Guid runId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (registry.TryGet(runId, out var live) && live.TryGetSnapshot(sessionId, out var snapshot))
        {
            return snapshot;
        }

        var session = await db.PlaybackSessions.AsNoTracking()
            .Include(s => s.MediaItem)
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.TestRunId == runId, cancellationToken)
            ?? throw new NotFoundException("Session", sessionId);

        return SessionSnapshot.From(session, session.MediaItem?.Title ?? string.Empty);
    }

    public async Task<IReadOnlyList<SessionEventDto>> GetSessionEventsAsync(Guid runId, Guid sessionId, CancellationToken cancellationToken)
    {
        var exists = await db.PlaybackSessions.AnyAsync(s => s.Id == sessionId && s.TestRunId == runId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Session", sessionId);
        }

        return await db.SessionEvents.AsNoTracking()
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new SessionEventDto(e.Id, e.FromStatus, e.ToStatus, e.Message, e.OccurredAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaItemDto>> ListMediaAsync(CancellationToken cancellationToken) =>
        await db.MediaItems.AsNoTracking()
            .OrderBy(m => m.Title)
            .Select(m => new MediaItemDto(m.Id, m.Title, m.Platform, m.ExternalId, m.DurationSeconds))
            .ToListAsync(cancellationToken);

    internal async Task<TestRun> FindRunAsync(Guid runId, CancellationToken cancellationToken) =>
        await db.TestRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, cancellationToken)
        ?? throw new NotFoundException("Test run", runId);

    private async Task EnsureRunExistsAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (!await db.TestRuns.AnyAsync(r => r.Id == runId, cancellationToken))
        {
            throw new NotFoundException("Test run", runId);
        }
    }

    private RunDto ToDto(TestRun run, DateTimeOffset now) =>
        RunDto.From(run, registry.TryGet(run.Id, out var live) ? live.GetStatistics() : null, now);

    /// <summary>Parses "Playing,Buffering", "active" or "terminal". Returns null for no filter.</summary>
    internal static HashSet<SessionStatus>? ParseStatuses(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = new HashSet<SessionStatus>();
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                result.UnionWith(Enum.GetValues<SessionStatus>().Where(s => s.IsActive()));
            }
            else if (token.Equals("terminal", StringComparison.OrdinalIgnoreCase))
            {
                result.UnionWith(Enum.GetValues<SessionStatus>().Where(s => s.IsTerminal()));
            }
            else if (Enum.TryParse<SessionStatus>(token, ignoreCase: true, out var status) && Enum.IsDefined(status))
            {
                result.Add(status);
            }
            else
            {
                throw new FluentValidation.ValidationException(
                    [new FluentValidation.Results.ValidationFailure("status", $"Unknown status '{token}'. Use a session status, 'active' or 'terminal'.")]);
            }
        }

        return result;
    }

    private static IEnumerable<SessionSnapshot> SortInMemory(IEnumerable<SessionSnapshot> source, string sortBy, bool desc)
    {
        Func<SessionSnapshot, object?> key = sortBy.ToUpperInvariant() switch
        {
            "STATUS" => s => s.Status,
            "PROGRESS" => s => s.ProgressPercent,
            "STARTEDAT" => s => s.StartedAt,
            "ENDEDAT" => s => s.EndedAt,
            "BYTES" => s => s.BytesReceived,
            "RETRIES" => s => s.RetryCount,
            _ => s => s.SequenceNumber,
        };

        var ordered = desc ? source.OrderByDescending(key) : source.OrderBy(key);
        return ordered.ThenBy(s => s.SequenceNumber);
    }

    private static IQueryable<PlaybackSession> SortInDatabase(IQueryable<PlaybackSession> source, string sortBy, bool desc) =>
        sortBy.ToUpperInvariant() switch
        {
            "STATUS" => Order(source, s => s.Status, desc),
            "PROGRESS" => Order(source, s => s.PositionSeconds / s.DurationSeconds, desc),
            "STARTEDAT" => Order(source, s => s.StartedAt, desc),
            "ENDEDAT" => Order(source, s => s.EndedAt, desc),
            "BYTES" => Order(source, s => s.BytesReceived, desc),
            "RETRIES" => Order(source, s => s.RetryCount, desc),
            _ => Order(source, s => s.SequenceNumber, desc),
        };

    private static IQueryable<PlaybackSession> Order<TKey>(IQueryable<PlaybackSession> source, System.Linq.Expressions.Expression<Func<PlaybackSession, TKey>> key, bool desc) =>
        (desc ? source.OrderByDescending(key) : source.OrderBy(key)).ThenBy(s => s.SequenceNumber);
}

using Microsoft.EntityFrameworkCore;
using StreamingAutomation.Domain.Entities;

namespace StreamingAutomation.Application.Common.Interfaces;

/// <summary>
/// Persistence abstraction used by application use cases.
/// Exposing <see cref="DbSet{TEntity}"/> (instead of hand-written repositories) is a deliberate,
/// pragmatic trade-off: EF Core already implements Unit of Work + Repository, and the Application
/// layer stays independent of the database provider (Npgsql lives only in Infrastructure).
/// </summary>
public interface IApplicationDbContext
{
    DbSet<TestRun> TestRuns { get; }

    DbSet<PlaybackSession> PlaybackSessions { get; }

    DbSet<MediaItem> MediaItems { get; }

    DbSet<SessionEvent> SessionEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

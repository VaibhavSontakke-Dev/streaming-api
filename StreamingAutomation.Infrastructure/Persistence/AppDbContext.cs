using Microsoft.EntityFrameworkCore;
using StreamingAutomation.Application.Common.Interfaces;
using StreamingAutomation.Domain.Entities;

namespace StreamingAutomation.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<TestRun> TestRuns => Set<TestRun>();

    public DbSet<PlaybackSession> PlaybackSessions => Set<PlaybackSession>();

    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    public DbSet<SessionEvent> SessionEvents => Set<SessionEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

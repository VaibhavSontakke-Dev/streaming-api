using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingAutomation.Domain.Entities;

namespace StreamingAutomation.Infrastructure.Persistence.Configurations;

internal sealed class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    public static readonly DateTimeOffset SeedTimestamp = new(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Title).HasMaxLength(300).IsRequired();
        builder.Property(m => m.Platform).HasMaxLength(50).IsRequired();
        builder.Property(m => m.ExternalId).HasMaxLength(100);

        // Synthetic test catalogue served by the local Test Media Server (no real platform content).
        builder.HasData(
            new MediaItem(Guid.Parse("8f1c2a10-0000-4000-8000-000000000001"), "Test Media 01 – Short clip", "YouTube", "test-media-01", 60, SeedTimestamp),
            new MediaItem(Guid.Parse("8f1c2a10-0000-4000-8000-000000000002"), "Test Media 02 – Music track", "YouTube", "test-media-02", 180, SeedTimestamp),
            new MediaItem(Guid.Parse("8f1c2a10-0000-4000-8000-000000000003"), "Test Media 03 – Podcast segment", "YouTube", "test-media-03", 300, SeedTimestamp),
            new MediaItem(Guid.Parse("8f1c2a10-0000-4000-8000-000000000004"), "Test Media 04 – Tutorial", "YouTube", "test-media-04", 240, SeedTimestamp),
            new MediaItem(Guid.Parse("8f1c2a10-0000-4000-8000-000000000005"), "Test Media 05 – Trailer", "YouTube", "test-media-05", 120, SeedTimestamp));
    }
}

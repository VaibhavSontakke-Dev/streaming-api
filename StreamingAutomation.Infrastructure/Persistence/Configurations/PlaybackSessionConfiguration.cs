using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingAutomation.Domain.Entities;

namespace StreamingAutomation.Infrastructure.Persistence.Configurations;

internal sealed class PlaybackSessionConfiguration : IEntityTypeConfiguration<PlaybackSession>
{
    public void Configure(EntityTypeBuilder<PlaybackSession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ErrorCode).HasMaxLength(100);
        builder.Property(s => s.ErrorMessage).HasMaxLength(1000);
        builder.Ignore(s => s.ProgressPercent);

        builder.HasOne(s => s.MediaItem)
            .WithMany()
            .HasForeignKey(s => s.MediaItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.TestRunId, s.SequenceNumber }).IsUnique();
        builder.HasIndex(s => new { s.TestRunId, s.Status });
    }
}

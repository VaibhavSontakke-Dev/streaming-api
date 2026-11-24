using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingAutomation.Domain.Entities;

namespace StreamingAutomation.Infrastructure.Persistence.Configurations;

internal sealed class TestRunConfiguration : IEntityTypeConfiguration<TestRun>
{
    public void Configure(EntityTypeBuilder<TestRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Platform).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(r => r.IsFinished);

        builder.HasMany(r => r.Sessions)
            .WithOne()
            .HasForeignKey(s => s.TestRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.CreatedAt);
        builder.HasIndex(r => r.Status);
    }
}

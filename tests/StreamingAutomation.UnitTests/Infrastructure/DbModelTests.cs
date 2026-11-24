using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Infrastructure.Persistence;

namespace StreamingAutomation.UnitTests.Infrastructure;

public class DbModelTests
{
    [Fact]
    public void Model_HasNoPendingChanges_ComparedToMigrations()
    {
        using var db = new DesignTimeDbContextFactoryAccessor().Create();
        Assert.False(db.Database.HasPendingModelChanges(), "Run `dotnet ef migrations add` – the model differs from the last migration.");
    }

    [Fact]
    public void SeedCatalogue_ContainsFiveTestMediaItems()
    {
        using var db = new DesignTimeDbContextFactoryAccessor().Create();
        var seed = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MediaItem))!.GetSeedData().ToList();
        Assert.Equal(5, seed.Count);
    }

    private sealed class DesignTimeDbContextFactoryAccessor
    {
        public AppDbContext Create() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .UseSnakeCaseNamingConvention()
                .Options);
    }
}

namespace StreamingAutomation.Domain.Entities;

/// <summary>
/// A media item a session can play. Items are served by the local Test Media Server;
/// <see cref="ExternalId"/> optionally references the real platform item for traceability only.
/// </summary>
public class MediaItem
{
    private MediaItem() { } // EF Core

    public MediaItem(Guid id, string title, string platform, string? externalId, int durationSeconds, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(durationSeconds, 0);

        Id = id;
        Title = title;
        Platform = platform;
        ExternalId = externalId;
        DurationSeconds = durationSeconds;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>"YouTube" or "Spotify" – the platform the item is modelled on.</summary>
    public string Platform { get; private set; } = string.Empty;

    public string? ExternalId { get; private set; }

    public int DurationSeconds { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

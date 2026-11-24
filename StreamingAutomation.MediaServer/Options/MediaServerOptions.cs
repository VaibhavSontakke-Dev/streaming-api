using System.ComponentModel.DataAnnotations;

namespace StreamingAutomation.MediaServer.Options;

/// <summary>Configuration for the local Test Media Server (section "MediaServer").</summary>
public sealed class MediaServerOptions
{
    public const string SectionName = "MediaServer";

    /// <summary>Length of one media segment (HLS/DASH-style chunk).</summary>
    [Range(1, 30)]
    public int SegmentDurationSeconds { get; set; } = 2;

    /// <summary>Simulated stream bitrate; determines segment payload size.</summary>
    [Range(8, 20_000)]
    public int BitrateKbps { get; set; } = 128;

    /// <summary>Fixed artificial latency added to every segment response.</summary>
    [Range(0, 10_000)]
    public int BaseLatencyMs { get; set; } = 15;

    /// <summary>Random extra latency (0..N ms) added on top of <see cref="BaseLatencyMs"/>.</summary>
    [Range(0, 10_000)]
    public int LatencyJitterMs { get; set; } = 35;

    /// <summary>Probability (0–1) that any single request gets a transient HTTP 503 (recoverable via retry).</summary>
    [Range(0.0, 1.0)]
    public double TransientErrorRate { get; set; } = 0.01;

    [MinLength(1)]
    public List<CatalogueEntry> Catalogue { get; set; } = [];

    public int SegmentSizeBytes => BitrateKbps * 1000 / 8 * SegmentDurationSeconds;
}

public sealed class CatalogueEntry
{
    [Required]
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    [Range(1, 86_400)]
    public int DurationSeconds { get; set; }
}

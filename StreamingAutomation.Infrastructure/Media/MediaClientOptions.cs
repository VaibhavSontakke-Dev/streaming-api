using System.ComponentModel.DataAnnotations;

namespace StreamingAutomation.Infrastructure.Media;

/// <summary>Settings for talking to the Test Media Server (section "MediaClient").</summary>
public sealed class MediaClientOptions
{
    public const string SectionName = "MediaClient";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:5200";

    /// <summary>Timeout for a single HTTP attempt.</summary>
    [Range(1, 120)]
    public int AttemptTimeoutSeconds { get; set; } = 5;

    /// <summary>Timeout for a request including all retries.</summary>
    [Range(1, 600)]
    public int TotalTimeoutSeconds { get; set; } = 30;

    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    [Range(10, 10_000)]
    public int RetryBaseDelayMs { get; set; } = 200;

    /// <summary>Upper bound on parallel TCP connections to the media server.</summary>
    [Range(1, 10_000)]
    public int MaxConnections { get; set; } = 1000;
}

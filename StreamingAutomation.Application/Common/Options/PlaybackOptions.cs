using System.ComponentModel.DataAnnotations;

namespace StreamingAutomation.Application.Common.Options;

/// <summary>Engine limits and timings (section "Playback").</summary>
public sealed class PlaybackOptions
{
    public const string SectionName = "Playback";

    /// <summary>Upper bound on sessions a single run may request.</summary>
    [Range(1, 10_000)]
    public int MaxSessionsPerRun { get; set; } = 1000;

    /// <summary>Host-wide cap on simultaneously executing sessions across all runs (protects the machine).</summary>
    [Range(1, 10_000)]
    public int MaxGlobalConcurrency { get; set; } = 1000;

    [Range(0.1, 1000)]
    public double MaxPlaybackSpeed { get; set; } = 100;

    /// <summary>How often buffered session state is written to the database.</summary>
    [Range(100, 60_000)]
    public int FlushIntervalMs { get; set; } = 1000;

    /// <summary>A session with no heartbeat for this long is failed by the watchdog.</summary>
    [Range(5, 3600)]
    public int StallTimeoutSeconds { get; set; } = 45;

    [Range(1, 60)]
    public int WatchdogIntervalSeconds { get; set; } = 5;

    /// <summary>How often live dashboards receive coalesced updates over SignalR.</summary>
    [Range(100, 60_000)]
    public int BroadcastIntervalMs { get; set; } = 1000;

    /// <summary>A segment fetch slower than max(segment play time, this) puts the session into Buffering.</summary>
    [Range(50, 60_000)]
    public int MinBufferingThresholdMs { get; set; } = 500;
}

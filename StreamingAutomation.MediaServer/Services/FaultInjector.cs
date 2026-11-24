using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StreamingAutomation.MediaServer.Options;

namespace StreamingAutomation.MediaServer.Services;

/// <summary>
/// Decides which requests fail. Two independent fault types:
/// <list type="bullet">
/// <item><b>Transient</b> – random HTTP 503 at <see cref="MediaServerOptions.TransientErrorRate"/>; clients should recover by retrying.</item>
/// <item><b>Broken stream</b> – a deterministic subset of sessions (chosen by hashing the session id against the
/// caller-supplied <c>sessionFailRate</c>) whose stream breaks permanently at a fixed segment. These sessions fail
/// even after retries. The outcome is predictable from the session id; ids are new per run, so counts vary between runs.</item>
/// </list>
/// </summary>
public sealed class FaultInjector(IOptionsMonitor<MediaServerOptions> options)
{
    public bool ShouldInjectTransientError() =>
        Random.Shared.NextDouble() < options.CurrentValue.TransientErrorRate;

    /// <summary>
    /// Returns the segment index at which the session's stream breaks, or null if it plays cleanly.
    /// Never breaks at segment 0 so that a broken session is still observed "playing" first.
    /// </summary>
    public static int? GetBreakingSegment(string sessionId, double sessionFailRate, int segmentCount)
    {
        if (sessionFailRate <= 0 || segmentCount < 2 || string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        var selector = BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue;
        if (selector >= sessionFailRate)
        {
            return null;
        }

        var position = BitConverter.ToUInt32(hash, 4);
        return 1 + (int)(position % (uint)(segmentCount - 1));
    }

    public TimeSpan NextLatency()
    {
        var o = options.CurrentValue;
        return TimeSpan.FromMilliseconds(o.BaseLatencyMs + (o.LatencyJitterMs > 0 ? Random.Shared.Next(o.LatencyJitterMs) : 0));
    }
}

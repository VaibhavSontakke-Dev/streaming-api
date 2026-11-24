using System.Collections.Concurrent;

namespace StreamingAutomation.MediaServer.Services;

/// <summary>
/// Server-side telemetry, measured independently of the orchestrator. This gives a second,
/// independent source of evidence for the number of concurrent streams actually being served.
/// </summary>
public sealed class StreamStatistics(TimeProvider clock)
{
    /// <summary>A stream counts as active if it requested something within this window.</summary>
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeen = new();
    private long _manifestRequests;
    private long _segmentRequests;
    private long _bytesServed;
    private long _transientErrors;
    private long _brokenStreamErrors;
    private int _peakActiveStreams;
    private long _completedStreams;

    public void RecordManifest(string sessionId)
    {
        Interlocked.Increment(ref _manifestRequests);
        Touch(sessionId);
    }

    /// <param name="sessionId">Stream identity (X-Session-Id).</param>
    /// <param name="bytes">Payload size served.</param>
    /// <param name="isFinalSegment">
    /// True when the last segment of the media was served: the stream has finished, so it stops counting as active
    /// immediately instead of lingering for <see cref="ActiveWindow"/> (keeps the count exact for short sessions).
    /// </param>
    public void RecordSegment(string sessionId, int bytes, bool isFinalSegment = false)
    {
        Interlocked.Increment(ref _segmentRequests);
        Interlocked.Add(ref _bytesServed, bytes);
        Touch(sessionId);
        if (isFinalSegment && !string.IsNullOrEmpty(sessionId))
        {
            _lastSeen.TryRemove(sessionId, out _);
            Interlocked.Increment(ref _completedStreams);
        }
    }

    public void RecordTransientError() => Interlocked.Increment(ref _transientErrors);

    public void RecordBrokenStream() => Interlocked.Increment(ref _brokenStreamErrors);

    public StatisticsSnapshot Snapshot()
    {
        var active = CountActive();
        return new StatisticsSnapshot(
            ActiveStreams: active,
            PeakActiveStreams: Math.Max(Volatile.Read(ref _peakActiveStreams), active),
            StreamsInWindow: _lastSeen.Count,
            CompletedStreams: Interlocked.Read(ref _completedStreams),
            ManifestRequests: Interlocked.Read(ref _manifestRequests),
            SegmentRequests: Interlocked.Read(ref _segmentRequests),
            BytesServed: Interlocked.Read(ref _bytesServed),
            TransientErrorsInjected: Interlocked.Read(ref _transientErrors),
            BrokenStreamErrorsInjected: Interlocked.Read(ref _brokenStreamErrors),
            MeasuredAt: clock.GetUtcNow());
    }

    public void Reset()
    {
        _lastSeen.Clear();
        Interlocked.Exchange(ref _manifestRequests, 0);
        Interlocked.Exchange(ref _segmentRequests, 0);
        Interlocked.Exchange(ref _bytesServed, 0);
        Interlocked.Exchange(ref _transientErrors, 0);
        Interlocked.Exchange(ref _brokenStreamErrors, 0);
        Interlocked.Exchange(ref _peakActiveStreams, 0);
        Interlocked.Exchange(ref _completedStreams, 0);
    }

    private void Touch(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        _lastSeen[sessionId] = clock.GetUtcNow();
        UpdatePeak(CountActive());
    }

    private int CountActive()
    {
        var cutoff = clock.GetUtcNow() - ActiveWindow;
        var count = 0;
        foreach (var seen in _lastSeen.Values)
        {
            if (seen >= cutoff)
            {
                count++;
            }
        }

        return count;
    }

    private void UpdatePeak(int candidate)
    {
        int current;
        while (candidate > (current = Volatile.Read(ref _peakActiveStreams)))
        {
            if (Interlocked.CompareExchange(ref _peakActiveStreams, candidate, current) == current)
            {
                return;
            }
        }
    }
}

public sealed record StatisticsSnapshot(
    int ActiveStreams,
    int PeakActiveStreams,
    int StreamsInWindow,
    long CompletedStreams,
    long ManifestRequests,
    long SegmentRequests,
    long BytesServed,
    long TransientErrorsInjected,
    long BrokenStreamErrorsInjected,
    DateTimeOffset MeasuredAt);

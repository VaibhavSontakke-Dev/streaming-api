namespace StreamingAutomation.Application.Playback;

/// <summary>Port to the media delivery service (implemented in Infrastructure over HTTP).</summary>
public interface IMediaStreamClient
{
    Task<MediaManifest> GetManifestAsync(string mediaKey, StreamRequestContext context, CancellationToken cancellationToken);

    /// <summary>Downloads (and discards) one segment; returns the number of bytes received.</summary>
    Task<long> DownloadSegmentAsync(string mediaKey, int segmentIndex, StreamRequestContext context, CancellationToken cancellationToken);
}

public sealed record MediaManifest(
    string Key,
    string Title,
    int DurationSeconds,
    int SegmentDurationSeconds,
    int SegmentCount,
    int SegmentSizeBytes,
    int BitrateKbps);

/// <summary>Per-session information sent with each media request.</summary>
/// <param name="SessionId">Identifies the stream to the media server (telemetry + deterministic fault injection).</param>
/// <param name="SessionFailRate">Fault-injection rate requested by the test run (0–1).</param>
/// <param name="Retries">Incremented by the resilience pipeline on every retry, so sessions can report retry counts.</param>
public sealed record StreamRequestContext(Guid SessionId, double SessionFailRate, RetryCounter Retries);

/// <summary>Thread-safe counter shared between the HTTP resilience pipeline and a session.</summary>
public sealed class RetryCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);
}

/// <summary>A media request that failed after all retries. <see cref="ErrorCode"/> is persisted on the session.</summary>
public sealed class MediaStreamException : Exception
{
    public MediaStreamException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException) => ErrorCode = errorCode;

    public MediaStreamException() { }

    public MediaStreamException(string message) : base(message) { }

    public MediaStreamException(string message, Exception innerException) : base(message, innerException) { }

    public string ErrorCode { get; } = "MEDIA_ERROR";
}

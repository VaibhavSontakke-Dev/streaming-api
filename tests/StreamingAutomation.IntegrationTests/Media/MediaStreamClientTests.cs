using System.Net.Http.Json;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.MediaServer.Services;

namespace StreamingAutomation.IntegrationTests.Media;

public class MediaStreamClientTests
{
    private static StreamRequestContext Ctx(double failRate = 0) => new(Guid.NewGuid(), failRate, new RetryCounter());

    [Fact]
    public async Task Manifest_DescribesSegmentsForMedia()
    {
        using var host = new MediaServerHost();
        var manifest = await host.Client.GetManifestAsync("test-media-01", Ctx(), CancellationToken.None);

        Assert.Equal(60, manifest.DurationSeconds);
        Assert.Equal(30, manifest.SegmentCount);
        Assert.Equal(32_000, manifest.SegmentSizeBytes); // 128 kbps * 2 s
    }

    [Fact]
    public async Task Segment_ReturnsExpectedBytes()
    {
        using var host = new MediaServerHost();
        var bytes = await host.Client.DownloadSegmentAsync("test-media-01", 0, Ctx(), CancellationToken.None);
        Assert.Equal(32_000, bytes);
    }

    [Fact]
    public async Task UnknownMedia_FailsWithHttpCode_WithoutRetrying()
    {
        using var host = new MediaServerHost();
        var ctx = Ctx();
        var ex = await Assert.ThrowsAsync<MediaStreamException>(() => host.Client.GetManifestAsync("nope", ctx, CancellationToken.None));
        Assert.Equal("MEDIA_HTTP_404", ex.ErrorCode);
        Assert.Equal(0, ctx.Retries.Count);
    }

    [Fact]
    public async Task TransientErrors_AreRetried_ThenSurfaceWithCode()
    {
        using var host = new MediaServerHost(transientErrorRate: 1.0, maxRetries: 2);
        var ctx = Ctx();
        var ex = await Assert.ThrowsAsync<MediaStreamException>(() => host.Client.DownloadSegmentAsync("test-media-01", 0, ctx, CancellationToken.None));
        Assert.Equal("TRANSIENT_UNAVAILABLE", ex.ErrorCode);
        Assert.Equal(2, ctx.Retries.Count);
    }

    [Fact]
    public async Task BrokenStream_PlaysFirstSegment_ThenFailsPermanently()
    {
        using var host = new MediaServerHost();
        var ctx = Ctx(failRate: 1.0);
        var breakAt = FaultInjector.GetBreakingSegment(ctx.SessionId.ToString(), 1.0, 30)!.Value;

        Assert.Equal(32_000, await host.Client.DownloadSegmentAsync("test-media-01", 0, ctx, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<MediaStreamException>(() => host.Client.DownloadSegmentAsync("test-media-01", breakAt, ctx, CancellationToken.None));
        Assert.Equal("STREAM_BROKEN", ex.ErrorCode);
    }

    [Fact]
    public async Task Cancellation_IsPropagated_NotWrapped()
    {
        using var host = new MediaServerHost();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.DownloadSegmentAsync("test-media-01", 0, Ctx(), cts.Token));
    }

    [Fact]
    public async Task ServerTelemetry_Observes150ConcurrentStreams()
    {
        using var host = new MediaServerHost();
        var sessions = Enumerable.Range(0, 150).Select(_ => Ctx()).ToList();

        await Task.WhenAll(sessions.Select(s => host.Client.DownloadSegmentAsync("test-media-02", 0, s, CancellationToken.None)));

        var stats = await host.RawClient().GetFromJsonAsync<StatisticsSnapshot>("/stats");
        Assert.NotNull(stats);
        Assert.Equal(150, stats!.ActiveStreams);
        Assert.Equal(150, stats.SegmentRequests);
        Assert.Equal(150L * 32_000, stats.BytesServed);
    }
}

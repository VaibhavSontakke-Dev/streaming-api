using System.Globalization;
using StreamingAutomation.MediaServer.Services;

namespace StreamingAutomation.MediaServer.Endpoints;

/// <summary>
/// HTTP surface of the Test Media Server. Clients identify themselves with headers:
/// <c>X-Session-Id</c> (required for telemetry) and <c>X-Session-Fail-Rate</c> (optional fault-injection rate 0–1).
/// </summary>
public static class MediaEndpoints
{
    public const string SessionIdHeader = "X-Session-Id";
    public const string SessionFailRateHeader = "X-Session-Fail-Rate";
    public const string BrokenStreamCode = "STREAM_BROKEN";
    public const string TransientCode = "TRANSIENT_UNAVAILABLE";

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var media = app.MapGroup("/media").WithTags("Media");

        media.MapGet("/", (MediaCatalogue catalogue) => Results.Ok(catalogue.All()));

        media.MapGet("/{key}/manifest", async (string key, HttpContext ctx, MediaCatalogue catalogue, FaultInjector faults, StreamStatistics stats, CancellationToken ct) =>
        {
            var manifest = catalogue.Find(key);
            if (manifest is null)
            {
                return Results.Problem($"Media '{key}' not found.", statusCode: StatusCodes.Status404NotFound);
            }

            await Task.Delay(faults.NextLatency(), ct);
            if (faults.ShouldInjectTransientError())
            {
                stats.RecordTransientError();
                return Transient();
            }

            stats.RecordManifest(SessionId(ctx));
            return Results.Ok(manifest);
        });

        media.MapGet("/{key}/segments/{index:int}", async (string key, int index, HttpContext ctx, MediaCatalogue catalogue, FaultInjector faults, StreamStatistics stats, CancellationToken ct) =>
        {
            var manifest = catalogue.Find(key);
            if (manifest is null)
            {
                return Results.Problem($"Media '{key}' not found.", statusCode: StatusCodes.Status404NotFound);
            }

            if (index < 0 || index >= manifest.SegmentCount)
            {
                return Results.Problem($"Segment {index} out of range (0..{manifest.SegmentCount - 1}).", statusCode: StatusCodes.Status416RangeNotSatisfiable);
            }

            await Task.Delay(faults.NextLatency(), ct);

            var sessionId = SessionId(ctx);
            var breakAt = FaultInjector.GetBreakingSegment(sessionId, FailRate(ctx), manifest.SegmentCount);
            if (breakAt is not null && index >= breakAt)
            {
                stats.RecordBrokenStream();
                return Results.Problem(
                    title: BrokenStreamCode,
                    detail: $"Injected fault: stream broken at segment {breakAt}.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            if (faults.ShouldInjectTransientError())
            {
                stats.RecordTransientError();
                return Transient();
            }

            // Last segment may be shorter than a full segment.
            var remainingSeconds = manifest.DurationSeconds - (index * manifest.SegmentDurationSeconds);
            var seconds = Math.Min(manifest.SegmentDurationSeconds, remainingSeconds);
            var size = manifest.SegmentSizeBytes * seconds / manifest.SegmentDurationSeconds;

            stats.RecordSegment(sessionId, size, isFinalSegment: index == manifest.SegmentCount - 1);
            return Results.Bytes(catalogue.Payload(size), "video/mp2t");
        });

        app.MapGet("/stats", (StreamStatistics stats) => Results.Ok(stats.Snapshot())).WithTags("Telemetry");
        app.MapPost("/stats/reset", (StreamStatistics stats) =>
        {
            stats.Reset();
            return Results.NoContent();
        }).WithTags("Telemetry");

        return app;
    }

    private static IResult Transient() =>
        Results.Problem(title: TransientCode, detail: "Injected transient fault.", statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string SessionId(HttpContext ctx) => ctx.Request.Headers[SessionIdHeader].ToString();

    private static double FailRate(HttpContext ctx) =>
        double.TryParse(ctx.Request.Headers[SessionFailRateHeader].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
            ? Math.Clamp(rate, 0, 1)
            : 0;
}

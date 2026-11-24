using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StreamingAutomation.Application.Common.Options;
using StreamingAutomation.Domain.Common;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Application.Playback;

/// <summary>Everything a worker needs to play one session.</summary>
public sealed record SessionAssignment(PlaybackSession Session, string MediaKey, string MediaTitle);

/// <summary>
/// Plays one session end-to-end: manifest → time-paced segment downloads → completion.
/// Never throws: every outcome ends in a terminal session status with an error code where relevant.
/// </summary>
public sealed class SessionWorker(
    IMediaStreamClient mediaClient,
    PlaybackPersistence persistence,
    IOptions<PlaybackOptions> options,
    TimeProvider clock,
    ILogger<SessionWorker> logger)
{
    public async Task RunAsync(RunContext run, SessionAssignment assignment, SessionHandle handle)
    {
        var session = assignment.Session;
        var token = handle.Token;
        var streamContext = new StreamRequestContext(session.Id, run.SimulatedFailureRate, new RetryCounter());

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = run.RunId, ["SessionId"] = session.Id });

        try
        {
            Transition(run, assignment, s => s.Start(clock.GetUtcNow()));

            var manifest = await mediaClient.GetManifestAsync(assignment.MediaKey, streamContext, token);
            SyncRetries(session, streamContext);
            Transition(run, assignment, s => s.BeginPlayback(clock.GetUtcNow()));

            var segmentPlayTime = TimeSpan.FromSeconds(manifest.SegmentDurationSeconds / run.PlaybackSpeed);
            var bufferingThreshold = TimeSpan.FromMilliseconds(Math.Max(segmentPlayTime.TotalMilliseconds, options.Value.MinBufferingThresholdMs));

            for (var index = 0; index < manifest.SegmentCount; index++)
            {
                var segmentStarted = clock.GetTimestamp();
                var bytes = await DownloadWithBufferingAsync(run, assignment, manifest.Key, index, streamContext, bufferingThreshold, token);

                SyncRetries(session, streamContext);
                var position = Math.Min(session.DurationSeconds, (index + 1) * manifest.SegmentDurationSeconds);
                session.RecordSegment(position, bytes, clock.GetUtcNow());
                run.Publish(SessionSnapshot.From(session, assignment.MediaTitle));

                // Real-time pacing: a segment "plays" for segmentDuration / speed before the next one is needed.
                var remaining = segmentPlayTime - clock.GetElapsedTime(segmentStarted);
                if (remaining > TimeSpan.Zero && index < manifest.SegmentCount - 1)
                {
                    await Task.Delay(remaining, clock, token);
                }
            }

            Transition(run, assignment, s => s.Complete(clock.GetUtcNow()));
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // We were asked to stop: the stop reason wins over however the transport reported the abort
            // (OperationCanceledException, or e.g. an IOException/HttpRequestException from a torn-down connection).
            var reason = handle.Reason != CancelReason.None ? handle.Reason : run.RunCancelReason;
            var now = clock.GetUtcNow();
            switch (reason)
            {
                case CancelReason.Stalled:
                    EndWith(run, assignment, s => s.Fail("SESSION_STALLED", $"No heartbeat for {options.Value.StallTimeoutSeconds}s; stopped by watchdog.", now));
                    break;
                case CancelReason.Shutdown:
                    EndWith(run, assignment, s => s.Interrupt(now), "Host shutting down");
                    break;
                default:
                    EndWith(run, assignment, s => s.Cancel(now), "Cancelled by user");
                    break;
            }
        }
        catch (MediaStreamException ex)
        {
            SyncRetries(session, streamContext);
            logger.LogWarning("Session {Sequence} failed: {ErrorCode} {Message}", session.SequenceNumber, ex.ErrorCode, ex.Message);
            EndWith(run, assignment, s => s.Fail(ex.ErrorCode, ex.Message, clock.GetUtcNow()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Session {Sequence} crashed unexpectedly", session.SequenceNumber);
            EndWith(run, assignment, s => s.Fail("UNEXPECTED_ERROR", ex.Message, clock.GetUtcNow()));
        }
    }

    /// <summary>
    /// Starts the download; if it has not finished within the buffering threshold the session is shown as
    /// Buffering until the segment arrives (e.g. slow server, or retries in progress).
    /// </summary>
    private async Task<long> DownloadWithBufferingAsync(
        RunContext run, SessionAssignment assignment, string mediaKey, int index, StreamRequestContext ctx, TimeSpan threshold, CancellationToken token)
    {
        var download = mediaClient.DownloadSegmentAsync(mediaKey, index, ctx, token);
        if (!download.IsCompleted)
        {
            using var thresholdCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var timer = Task.Delay(threshold, clock, thresholdCts.Token);
            var first = await Task.WhenAny(download, timer);
            await thresholdCts.CancelAsync();

            if (first != download && !download.IsCompleted)
            {
                Transition(run, assignment, s => s.EnterBuffering(clock.GetUtcNow()));
                try
                {
                    return await download;
                }
                finally
                {
                    if (assignment.Session.Status == SessionStatus.Buffering && download.IsCompletedSuccessfully)
                    {
                        Transition(run, assignment, s => s.ResumePlayback(clock.GetUtcNow()));
                    }
                }
            }
        }

        return await download;
    }

    private void Transition(RunContext run, SessionAssignment assignment, Action<PlaybackSession> change, string? message = null)
    {
        var session = assignment.Session;
        var from = session.Status;
        change(session);
        run.Publish(SessionSnapshot.From(session, assignment.MediaTitle));
        persistence.Enqueue(new SessionEvent(session.Id, session.TestRunId, from, session.Status, message ?? session.ErrorCode, clock.GetUtcNow()));
    }

    private void EndWith(RunContext run, SessionAssignment assignment, Action<PlaybackSession> change, string? message = null)
    {
        if (!assignment.Session.Status.IsTerminal())
        {
            Transition(run, assignment, change, message);
        }
    }

    private static void SyncRetries(PlaybackSession session, StreamRequestContext ctx)
    {
        while (session.RetryCount < ctx.Retries.Count)
        {
            session.RecordRetry();
        }
    }
}

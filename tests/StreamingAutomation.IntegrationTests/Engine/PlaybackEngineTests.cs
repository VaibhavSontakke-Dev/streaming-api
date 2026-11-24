using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Application.Runs;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;
using StreamingAutomation.MediaServer.Services;
using Xunit.Abstractions;

namespace StreamingAutomation.IntegrationTests.Engine;

[Collection(ApiCollection.Name)]
public class PlaybackEngineTests(ApiFactory api, ITestOutputHelper output)
{
    private static readonly Guid ShortClip = Guid.Parse("8f1c2a10-0000-4000-8000-000000000001"); // 60 s => 30 segments

    [Fact]
    public async Task Run_With150ConcurrentSessions_CompletesAndPersistsAccurateResults()
    {
        await api.Media.RawClient().PostAsync(new Uri("/stats/reset", UriKind.Relative), null);
        var started = DateTimeOffset.UtcNow;

        var runId = await StartRunAsync(new CreateRunRequest(
            "IT - 150 concurrent", SessionCount: 150, MaxConcurrency: 150, PlaybackSpeed: 30,
            SimulatedFailureRate: 0.1, MediaItemIds: [ShortClip]));

        await api.WaitForRunToFinishAsync(runId, TimeSpan.FromSeconds(90));
        var elapsed = DateTimeOffset.UtcNow - started;

        await using var db = api.NewDbContext();
        var run = await db.TestRuns.SingleAsync(r => r.Id == runId);
        var sessions = await db.PlaybackSessions.Where(s => s.TestRunId == runId).ToListAsync();
        var events = await db.SessionEvents.CountAsync(e => e.TestRunId == runId);
        var mediaStats = await api.Media.RawClient().GetFromJsonAsync<StatisticsSnapshot>("/stats");

        // Failures are deterministic: exactly the sessions the media server's fault injector selects.
        var expectedFailures = sessions.Count(s => FaultInjector.GetBreakingSegment(s.Id.ToString(), 0.1, 30) is not null);

        output.WriteLine(
            $"Elapsed {elapsed.TotalSeconds:F1}s | started {run.StartedCount} completed {run.CompletedCount} failed {run.FailedCount} " +
            $"(expected {expectedFailures}) peak {run.PeakConcurrency} | server peak streams {mediaStats!.PeakActiveStreams} | events {events}");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(150, run.StartedCount);
        Assert.Equal(150, run.CompletedCount + run.FailedCount);
        Assert.Equal(expectedFailures, run.FailedCount);
        Assert.True(run.PeakConcurrency >= 100, $"peak concurrency {run.PeakConcurrency}");
        Assert.True(mediaStats.PeakActiveStreams >= 100, $"server-observed peak {mediaStats.PeakActiveStreams}");

        Assert.Equal(150, sessions.Count);
        Assert.All(sessions, s => Assert.True(s.Status is SessionStatus.Completed or SessionStatus.Failed, $"{s.SequenceNumber}: {s.Status}"));
        Assert.All(sessions.Where(s => s.Status == SessionStatus.Completed), s =>
        {
            Assert.Equal(60, s.PositionSeconds);
            Assert.Equal(30 * 32_000, s.BytesReceived);
            Assert.NotNull(s.StartedAt);
            Assert.NotNull(s.EndedAt);
        });
        Assert.All(sessions.Where(s => s.Status == SessionStatus.Failed), s => Assert.Equal("STREAM_BROKEN", s.ErrorCode));
        Assert.Equal(run.TotalBytesReceived, sessions.Sum(s => s.BytesReceived));
        Assert.True(events >= 150 * 3, $"events {events}"); // Starting, Playing, terminal (+ buffering)
    }

    [Fact]
    public async Task CancelRun_StopsActiveAndPendingSessions()
    {
        var runId = await StartRunAsync(new CreateRunRequest(
            "IT - cancel run", SessionCount: 40, MaxConcurrency: 10, PlaybackSpeed: 1, MediaItemIds: [ShortClip]));

        var registry = api.Services.GetRequiredService<RunRegistry>();
        await WaitUntilAsync(() => registry.TryGet(runId, out var ctx) && ctx.GetStatistics().Active == 10);

        Assert.True(api.Services.GetRequiredService<IPlaybackOrchestrator>().CancelRun(runId));
        await api.WaitForRunToFinishAsync(runId, TimeSpan.FromSeconds(30));

        await using var db = api.NewDbContext();
        var run = await db.TestRuns.SingleAsync(r => r.Id == runId);
        var statuses = await db.PlaybackSessions.Where(s => s.TestRunId == runId).Select(s => s.Status).ToListAsync();

        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.Equal(10, run.StartedCount);
        Assert.Equal(40, run.CancelledCount);
        Assert.Equal(10, run.PeakConcurrency); // concurrency limit respected
        Assert.All(statuses, s => Assert.Equal(SessionStatus.Cancelled, s));
    }

    [Fact]
    public async Task CancelSingleSession_LeavesOthersRunning()
    {
        var runId = await StartRunAsync(new CreateRunRequest(
            "IT - cancel session", SessionCount: 5, PlaybackSpeed: 1, MediaItemIds: [ShortClip]));
        var registry = api.Services.GetRequiredService<RunRegistry>();
        await WaitUntilAsync(() => registry.TryGet(runId, out var c) && c.Snapshots.All(s => s.Status == SessionStatus.Playing));

        Assert.True(registry.TryGet(runId, out var ctx));
        var victim = ctx.Snapshots.Single(s => s.SequenceNumber == 1).Id;
        var orchestrator = api.Services.GetRequiredService<IPlaybackOrchestrator>();

        Assert.True(orchestrator.CancelSession(runId, victim));
        await WaitUntilAsync(() => ctx.TryGetSnapshot(victim, out var s) && s.Status == SessionStatus.Cancelled);
        Assert.Equal(4, ctx.GetStatistics().Active);

        orchestrator.CancelRun(runId);
        await api.WaitForRunToFinishAsync(runId, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task StartupRecovery_MarksOrphanedWorkAsInterrupted()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new TestRun(Guid.NewGuid(), "orphan", "YouTube", 1, 1, 0, 1, 0, now);
        run.Start(now);
        var session = new PlaybackSession(Guid.NewGuid(), run.Id, ShortClip, 1, 60, now);
        session.Start(now);
        session.BeginPlayback(now);

        await using (var db = api.NewDbContext())
        {
            db.TestRuns.Add(run);
            db.PlaybackSessions.Add(session);
            await db.SaveChangesAsync();
        }

        var (runs, sessions) = await api.Services.GetRequiredService<IPlaybackStateWriter>()
            .RecoverInterruptedAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.True(runs >= 1 && sessions >= 1);

        await using var verify = api.NewDbContext();
        Assert.Equal(RunStatus.Interrupted, (await verify.TestRuns.SingleAsync(r => r.Id == run.Id)).Status);
        Assert.Equal(SessionStatus.Interrupted, (await verify.PlaybackSessions.SingleAsync(s => s.Id == session.Id)).Status);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutSeconds = 20)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (!condition())
        {
            await Task.Delay(50, cts.Token);
        }
    }

    private async Task<Guid> StartRunAsync(CreateRunRequest request)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestRunService>().CreateAndStartAsync(request, CancellationToken.None);
    }
}

using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using StreamingAutomation.Application.Common.Interfaces;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Application.Runs;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Application.Reports;

public sealed class ReportService(IApplicationDbContext db, RunQueries queries, RunRegistry registry, TimeProvider clock)
{
    public const string DataSourceDisclaimer =
        "Simulated playback sessions measured locally against the Test Media Server (authorised test environment). " +
        "These figures are engine telemetry, NOT views or streams reported by YouTube or Spotify.";

    private const int MaxTimelinePoints = 120;

    public async Task<(RunReport Report, IReadOnlyList<SessionSnapshot> Sessions)> BuildAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await queries.GetRunAsync(runId, cancellationToken);
        var sessions = await LoadSessionsAsync(runId, cancellationToken);
        var now = clock.GetUtcNow();

        var started = sessions.Where(s => s.StartedAt is not null).ToList();
        var completedDurations = sessions
            .Where(s => s.Status == SessionStatus.Completed && s.StartedAt is not null && s.EndedAt is not null)
            .Select(s => (s.EndedAt!.Value - s.StartedAt!.Value).TotalSeconds)
            .ToList();
        var startupLatencies = sessions
            .Where(s => s.StartedAt is not null && s.PlaybackStartedAt is not null)
            .Select(s => (s.PlaybackStartedAt!.Value - s.StartedAt!.Value).TotalMilliseconds)
            .ToList();

        var totalBytes = sessions.Sum(s => s.BytesReceived);
        var runSeconds = run.DurationSeconds;
        var summary = new ReportSummary(
            run.RequestedSessions,
            run.Started,
            run.Completed,
            run.Failed,
            run.Cancelled,
            run.Interrupted,
            run.Active,
            SuccessRatePercent: run.Started == 0 ? 0 : Math.Round(run.Completed * 100.0 / run.Started, 2),
            run.PeakConcurrency,
            ObservedPeakConcurrency: ComputePeak(started, now),
            totalBytes,
            TotalSegments: sessions.Sum(s => (long)s.SegmentsReceived),
            TotalRetries: sessions.Sum(s => s.RetryCount),
            SessionsWithRetries: sessions.Count(s => s.RetryCount > 0),
            runSeconds,
            ThroughputKbps: runSeconds is > 0 ? Math.Round(totalBytes * 8 / 1000.0 / runSeconds.Value, 1) : 0);

        var report = new RunReport(
            run,
            summary,
            Stats(completedDurations, 1),
            Stats(startupLatencies, 0),
            sessions.GroupBy(s => s.Status).OrderBy(g => g.Key).Select(g => new StatusCount(g.Key, g.Count())).ToList(),
            sessions.Where(s => s.ErrorCode is not null)
                .GroupBy(s => s.ErrorCode!)
                .OrderByDescending(g => g.Count())
                .Select(g => new ErrorCount(g.Key, g.Count(), g.First().ErrorMessage))
                .ToList(),
            BuildTimeline(run, started, now),
            new EnvironmentInfo(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, Environment.ProcessorCount, RuntimeInformation.ProcessArchitecture.ToString()),
            DataSourceDisclaimer,
            now);

        return (report, sessions);
    }

    private async Task<IReadOnlyList<SessionSnapshot>> LoadSessionsAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (registry.TryGet(runId, out var live))
        {
            return live.Snapshots.OrderBy(s => s.SequenceNumber).ToList();
        }

        var entities = await db.PlaybackSessions.AsNoTracking()
            .Include(s => s.MediaItem)
            .Where(s => s.TestRunId == runId)
            .OrderBy(s => s.SequenceNumber)
            .ToListAsync(cancellationToken);
        return entities.Select(s => SessionSnapshot.From(s, s.MediaItem?.Title ?? string.Empty)).ToList();
    }

    /// <summary>Exact peak via a sweep over start (+1) / end (−1) events; ends are processed before starts at equal times.</summary>
    internal static int ComputePeak(IEnumerable<SessionSnapshot> started, DateTimeOffset now)
    {
        var events = new List<(DateTimeOffset At, int Delta)>();
        foreach (var s in started)
        {
            events.Add((s.StartedAt!.Value, +1));
            events.Add((s.EndedAt ?? now, -1));
        }

        var peak = 0;
        var current = 0;
        foreach (var (_, delta) in events.OrderBy(e => e.At).ThenBy(e => e.Delta))
        {
            current += delta;
            peak = Math.Max(peak, current);
        }

        return peak;
    }

    private static List<ConcurrencyPoint> BuildTimeline(RunDto run, List<SessionSnapshot> started, DateTimeOffset now)
    {
        if (run.StartedAt is not { } start)
        {
            return [];
        }

        var end = run.EndedAt ?? now;
        var span = end - start;
        var step = TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling(span.TotalSeconds / MaxTimelinePoints)));
        var points = new List<ConcurrencyPoint>();
        for (var t = start; t <= end + step; t += step)
        {
            var at = t > end ? end : t;
            var active = started.Count(s => s.StartedAt <= at && (s.EndedAt is null || s.EndedAt > at));
            points.Add(new ConcurrencyPoint(at, Math.Round((at - start).TotalSeconds, 1), active));
            if (at == end)
            {
                break;
            }
        }

        return points;
    }

    private static DurationStats Stats(List<double> values, int decimals)
    {
        if (values.Count == 0)
        {
            return new DurationStats(0, null, null, null, null, null);
        }

        values.Sort();
        double Round(double v) => Math.Round(v, decimals);
        double Percentile(double p) => values[Math.Clamp((int)Math.Ceiling(p * values.Count) - 1, 0, values.Count - 1)];

        return new DurationStats(values.Count, Round(values[0]), Round(values.Average()), Round(Percentile(0.5)), Round(Percentile(0.95)), Round(values[^1]));
    }
}

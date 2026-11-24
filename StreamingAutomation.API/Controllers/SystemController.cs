using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StreamingAutomation.Application.Playback;

namespace StreamingAutomation.API.Controllers;

/// <summary>Engine and process telemetry (observability).</summary>
[ApiController]
[Route("api/system")]
[Produces("application/json")]
public sealed class SystemController(RunRegistry registry, PlaybackPersistence persistence, TimeProvider clock) : ControllerBase
{
    private static readonly DateTimeOffset ProcessStarted = DateTimeOffset.UtcNow;

    /// <summary>Current engine load and process resource usage.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<SystemStatsDto>(StatusCodes.Status200OK)]
    public SystemStatsDto Stats()
    {
        var runs = registry.Active.Select(r => r.GetStatistics()).ToList();
        using var process = Process.GetCurrentProcess();
        ThreadPool.GetAvailableThreads(out var workerAvailable, out _);
        ThreadPool.GetMaxThreads(out var workerMax, out _);

        return new SystemStatsDto(
            ActiveRuns: runs.Count,
            ActiveSessions: runs.Sum(r => r.Active),
            PendingSessions: runs.Sum(r => r.Pending),
            PersistenceFlushes: persistence.FlushCount,
            SessionRowsWritten: persistence.SessionRowsWritten,
            WorkingSetMb: Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
            ManagedHeapMb: Math.Round(GC.GetTotalMemory(false) / 1024d / 1024d, 1),
            ProcessThreads: process.Threads.Count,
            ThreadPoolThreads: ThreadPool.ThreadCount,
            ThreadPoolBusyWorkers: workerMax - workerAvailable,
            CpuTimeSeconds: Math.Round(process.TotalProcessorTime.TotalSeconds, 1),
            UptimeSeconds: Math.Round((clock.GetUtcNow() - ProcessStarted).TotalSeconds),
            MeasuredAt: clock.GetUtcNow());
    }
}

public sealed record SystemStatsDto(
    int ActiveRuns,
    int ActiveSessions,
    int PendingSessions,
    long PersistenceFlushes,
    long SessionRowsWritten,
    double WorkingSetMb,
    double ManagedHeapMb,
    int ProcessThreads,
    int ThreadPoolThreads,
    int ThreadPoolBusyWorkers,
    double CpuTimeSeconds,
    double UptimeSeconds,
    DateTimeOffset MeasuredAt);

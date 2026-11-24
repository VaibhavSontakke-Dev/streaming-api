using StreamingAutomation.Application.Runs;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Application.Reports;

/// <summary>Results report for one test run – the "measurable results" deliverable.</summary>
public sealed record RunReport(
    RunDto Run,
    ReportSummary Summary,
    DurationStats SessionDurationSeconds,
    DurationStats StartupLatencyMs,
    IReadOnlyList<StatusCount> StatusBreakdown,
    IReadOnlyList<ErrorCount> Errors,
    IReadOnlyList<ConcurrencyPoint> ConcurrencyTimeline,
    EnvironmentInfo Environment,
    string DataSource,
    DateTimeOffset GeneratedAt);

/// <param name="SuccessRatePercent">Completed / started.</param>
/// <param name="ObservedPeakConcurrency">Max simultaneous sessions reconstructed from persisted start/end timestamps.</param>
/// <param name="ThroughputKbps">Aggregate media throughput across all sessions while the run was active.</param>
public sealed record ReportSummary(
    int Requested,
    int Started,
    int Completed,
    int Failed,
    int Cancelled,
    int Interrupted,
    int Active,
    double SuccessRatePercent,
    int PeakConcurrency,
    int ObservedPeakConcurrency,
    long TotalBytesReceived,
    long TotalSegments,
    int TotalRetries,
    int SessionsWithRetries,
    double? RunDurationSeconds,
    double ThroughputKbps);

public sealed record DurationStats(int Samples, double? Min, double? Average, double? P50, double? P95, double? Max);

public sealed record StatusCount(SessionStatus Status, int Count);

public sealed record ErrorCount(string ErrorCode, int Count, string? SampleMessage);

public sealed record ConcurrencyPoint(DateTimeOffset Timestamp, double OffsetSeconds, int ActiveSessions);

public sealed record EnvironmentInfo(string OperatingSystem, string Framework, int ProcessorCount, string ProcessArchitecture);

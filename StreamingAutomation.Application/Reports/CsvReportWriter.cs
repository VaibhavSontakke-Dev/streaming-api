using System.Globalization;
using System.Text;
using StreamingAutomation.Application.Playback;

namespace StreamingAutomation.Application.Reports;

/// <summary>
/// CSV export: a "metric,value" summary block, a blank line, then one row per session.
/// Values are RFC 4180-quoted and protected against spreadsheet formula injection.
/// </summary>
public static class CsvReportWriter
{
    private static readonly string[] SessionHeader =
    [
        "sequence", "session_id", "media_title", "status", "started_at_utc", "playback_started_at_utc", "ended_at_utc",
        "duration_seconds", "position_seconds", "progress_percent", "bytes_received", "segments_received", "retry_count",
        "error_code", "error_message",
    ];

    public static string Write(RunReport report, IReadOnlyList<SessionSnapshot> sessions)
    {
        var sb = new StringBuilder();
        var run = report.Run;
        var s = report.Summary;

        Row(sb, "metric", "value");
        Row(sb, "run_id", run.Id);
        Row(sb, "run_name", run.Name);
        Row(sb, "status", run.Status);
        Row(sb, "platform", run.Platform);
        Row(sb, "data_source", report.DataSource);
        Row(sb, "requested_sessions", s.Requested);
        Row(sb, "max_concurrency", run.MaxConcurrency);
        Row(sb, "playback_speed", run.PlaybackSpeed);
        Row(sb, "simulated_failure_rate", run.SimulatedFailureRate);
        Row(sb, "sessions_started", s.Started);
        Row(sb, "sessions_completed", s.Completed);
        Row(sb, "sessions_failed", s.Failed);
        Row(sb, "sessions_cancelled", s.Cancelled);
        Row(sb, "sessions_interrupted", s.Interrupted);
        Row(sb, "sessions_active", s.Active);
        Row(sb, "success_rate_percent", s.SuccessRatePercent);
        Row(sb, "peak_concurrency", s.PeakConcurrency);
        Row(sb, "observed_peak_concurrency", s.ObservedPeakConcurrency);
        Row(sb, "total_bytes_received", s.TotalBytesReceived);
        Row(sb, "total_retries", s.TotalRetries);
        Row(sb, "run_started_at_utc", run.StartedAt);
        Row(sb, "run_ended_at_utc", run.EndedAt);
        Row(sb, "run_duration_seconds", s.RunDurationSeconds);
        Row(sb, "throughput_kbps", s.ThroughputKbps);
        Row(sb, "generated_at_utc", report.GeneratedAt);
        sb.Append("\r\n");

        Row(sb, SessionHeader);
        foreach (var x in sessions)
        {
            double? duration = x.StartedAt is { } st && x.EndedAt is { } en ? Math.Round((en - st).TotalSeconds, 3) : null;
            Row(sb,
                x.SequenceNumber, x.Id, x.MediaTitle, x.Status, x.StartedAt, x.PlaybackStartedAt, x.EndedAt,
                duration, x.PositionSeconds, x.ProgressPercent, x.BytesReceived, x.SegmentsReceived, x.RetryCount,
                x.ErrorCode, x.ErrorMessage);
        }

        return sb.ToString();
    }

    private static void Row(StringBuilder sb, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(Escape(Format(values[i])));
        }

        sb.Append("\r\n");
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset d => d.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    internal static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '@' or '\t' or '\r' || (value.Length > 1 && value[0] == '-' && !char.IsDigit(value[1])))
        {
            value = "'" + value; // neutralise formula injection in Excel/Sheets
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}

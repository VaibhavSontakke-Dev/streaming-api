using StreamingAutomation.Domain.Entities;

namespace StreamingAutomation.Application.Playback;

/// <summary>Bulk persistence of hot engine state (implemented with set-based SQL in Infrastructure).</summary>
public interface IPlaybackStateWriter
{
    /// <summary>Writes the latest state of many sessions, appends audit events and updates run counters – in one batch.</summary>
    Task WriteAsync(
        IReadOnlyCollection<SessionSnapshot> sessions,
        IReadOnlyCollection<SessionEvent> events,
        IReadOnlyCollection<RunStatistics> runs,
        CancellationToken cancellationToken);

    /// <summary>Marks runs/sessions left in a non-terminal state by a previous process as Interrupted.</summary>
    Task<(int Runs, int Sessions)> RecoverInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

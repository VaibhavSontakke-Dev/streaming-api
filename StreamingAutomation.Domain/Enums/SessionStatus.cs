namespace StreamingAutomation.Domain.Enums;

/// <summary>
/// Lifecycle of a single simulated playback session.
/// Pending → Starting → Playing ⇄ Buffering → Completed | Failed | Cancelled | Interrupted.
/// </summary>
public enum SessionStatus
{
    Pending = 0,
    Starting = 1,
    Playing = 2,
    Buffering = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,

    /// <summary>The host stopped (crash/restart) while the session was in flight.</summary>
    Interrupted = 7,
}

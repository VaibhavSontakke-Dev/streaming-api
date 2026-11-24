using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Domain.Common;

/// <summary>
/// Single source of truth for which session status transitions are legal.
/// Kept as a table so it is trivially reviewable and unit-testable.
/// </summary>
public static class SessionStateMachine
{
    private static readonly Dictionary<SessionStatus, SessionStatus[]> Allowed = new()
    {
        [SessionStatus.Pending] = [SessionStatus.Starting, SessionStatus.Cancelled, SessionStatus.Interrupted],
        [SessionStatus.Starting] = [SessionStatus.Playing, SessionStatus.Failed, SessionStatus.Cancelled, SessionStatus.Interrupted],
        [SessionStatus.Playing] = [SessionStatus.Buffering, SessionStatus.Completed, SessionStatus.Failed, SessionStatus.Cancelled, SessionStatus.Interrupted],
        [SessionStatus.Buffering] = [SessionStatus.Playing, SessionStatus.Failed, SessionStatus.Cancelled, SessionStatus.Interrupted],
        [SessionStatus.Completed] = [],
        [SessionStatus.Failed] = [],
        [SessionStatus.Cancelled] = [],
        [SessionStatus.Interrupted] = [],
    };

    public static bool CanTransition(SessionStatus from, SessionStatus to) =>
        Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    public static bool IsTerminal(this SessionStatus status) =>
        status is SessionStatus.Completed or SessionStatus.Failed or SessionStatus.Cancelled or SessionStatus.Interrupted;

    /// <summary>Statuses that count towards "currently active" concurrency.</summary>
    public static bool IsActive(this SessionStatus status) =>
        status is SessionStatus.Starting or SessionStatus.Playing or SessionStatus.Buffering;
}

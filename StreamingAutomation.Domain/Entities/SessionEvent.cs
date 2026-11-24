using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.Domain.Entities;

/// <summary>Append-only audit record of a session status change (timeline / troubleshooting).</summary>
public class SessionEvent
{
    private SessionEvent() { } // EF Core

    public SessionEvent(Guid sessionId, Guid testRunId, SessionStatus? fromStatus, SessionStatus toStatus, string? message, DateTimeOffset occurredAt)
    {
        SessionId = sessionId;
        TestRunId = testRunId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Message = message is { Length: > 1000 } ? message[..1000] : message;
        OccurredAt = occurredAt;
    }

    public long Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid TestRunId { get; private set; }

    public SessionStatus? FromStatus { get; private set; }

    public SessionStatus ToStatus { get; private set; }

    public string? Message { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

namespace StreamingAutomation.Domain.Exceptions;

/// <summary>Base type for violations of domain rules (mapped to HTTP 409/422 by the API).</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) { }

    public DomainException() { }
}

/// <summary>Thrown when a state transition is not allowed by the state machine.</summary>
public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string entity, object from, object to)
        : base($"{entity} cannot transition from {from} to {to}.")
    {
        From = from.ToString() ?? string.Empty;
        To = to.ToString() ?? string.Empty;
    }

    public InvalidStateTransitionException() { }

    public InvalidStateTransitionException(string message) : base(message) { }

    public InvalidStateTransitionException(string message, Exception innerException) : base(message, innerException) { }

    public string From { get; } = string.Empty;

    public string To { get; } = string.Empty;
}

namespace StreamingAutomation.Domain.Enums;

/// <summary>Lifecycle of a test run (a batch of concurrent playback sessions).</summary>
public enum RunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,

    /// <summary>The host stopped while the run was in progress.</summary>
    Interrupted = 4,
}

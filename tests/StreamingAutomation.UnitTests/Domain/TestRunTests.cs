using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;
using StreamingAutomation.Domain.Exceptions;

namespace StreamingAutomation.UnitTests.Domain;

public class TestRunTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    private static TestRun NewRun() => new(Guid.NewGuid(), "run", "YouTube", 150, 150, 10, 10, 0.05, T0);

    [Fact]
    public void Lifecycle_PendingRunningCompleted()
    {
        var run = NewRun();
        run.Start(T0.AddSeconds(1));
        run.Complete(T0.AddSeconds(60));

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.True(run.IsFinished);
        Assert.Equal(T0.AddSeconds(60), run.EndedAt);
    }

    [Fact]
    public void Completing_APendingRun_Throws() =>
        Assert.Throws<InvalidStateTransitionException>(() => NewRun().Complete(T0));

    [Fact]
    public void PeakConcurrency_IsMonotonic()
    {
        var run = NewRun();
        run.UpdateStatistics(10, 0, 0, 0, 0, 120, 0);
        run.UpdateStatistics(150, 140, 10, 0, 0, 30, 0);
        Assert.Equal(120, run.PeakConcurrency);
        Assert.Equal(140, run.CompletedCount);
    }

    [Theory]
    [InlineData(0, 10, 1.0, 0.0)]
    [InlineData(10, 0, 1.0, 0.0)]
    [InlineData(10, 10, 0.0, 0.0)]
    [InlineData(10, 10, 1.0, 1.5)]
    public void InvalidConfiguration_Throws(int sessions, int concurrency, double speed, double failureRate) =>
        Assert.ThrowsAny<ArgumentException>(() => new TestRun(Guid.NewGuid(), "r", "YouTube", sessions, concurrency, 0, speed, failureRate, T0));
}

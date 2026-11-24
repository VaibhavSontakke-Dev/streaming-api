using StreamingAutomation.MediaServer.Services;

namespace StreamingAutomation.UnitTests.MediaServer;

public class FaultInjectorTests
{
    [Fact]
    public void BreakingSegment_IsDeterministic_ForSameSession()
    {
        var id = Guid.NewGuid().ToString();
        Assert.Equal(FaultInjector.GetBreakingSegment(id, 1.0, 50), FaultInjector.GetBreakingSegment(id, 1.0, 50));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void ZeroRate_NeverBreaks(double rate) =>
        Assert.Null(FaultInjector.GetBreakingSegment(Guid.NewGuid().ToString(), rate, 50));

    [Fact]
    public void FullRate_AlwaysBreaks_ButNeverAtFirstSegment()
    {
        for (var i = 0; i < 1000; i++)
        {
            var at = FaultInjector.GetBreakingSegment(Guid.NewGuid().ToString(), 1.0, 30);
            Assert.NotNull(at);
            Assert.InRange(at!.Value, 1, 29);
        }
    }

    [Fact]
    public void ObservedFailureRatio_MatchesRequestedRate()
    {
        const int n = 20_000;
        var broken = Enumerable.Range(0, n).Count(_ => FaultInjector.GetBreakingSegment(Guid.NewGuid().ToString(), 0.2, 30) is not null);
        Assert.InRange(broken / (double)n, 0.18, 0.22);
    }
}

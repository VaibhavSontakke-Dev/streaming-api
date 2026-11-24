using StreamingAutomation.MediaServer.Services;

namespace StreamingAutomation.UnitTests.MediaServer;

public class StreamStatisticsTests
{
    [Fact]
    public void FinishedStreams_StopCountingAsActive_ButPeakIsKept()
    {
        var stats = new StreamStatistics(TimeProvider.System);
        for (var i = 0; i < 5; i++)
        {
            stats.RecordSegment($"s{i}", 100);
        }

        stats.RecordSegment("s0", 100, isFinalSegment: true);
        stats.RecordSegment("s1", 100, isFinalSegment: true);

        var snapshot = stats.Snapshot();
        Assert.Equal(3, snapshot.ActiveStreams);
        Assert.Equal(5, snapshot.PeakActiveStreams);
        Assert.Equal(2, snapshot.CompletedStreams);
        Assert.Equal(700, snapshot.BytesServed);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var stats = new StreamStatistics(TimeProvider.System);
        stats.RecordSegment("s", 10);
        stats.Reset();
        var snapshot = stats.Snapshot();
        Assert.Equal(0, snapshot.ActiveStreams);
        Assert.Equal(0, snapshot.PeakActiveStreams);
        Assert.Equal(0, snapshot.BytesServed);
    }
}

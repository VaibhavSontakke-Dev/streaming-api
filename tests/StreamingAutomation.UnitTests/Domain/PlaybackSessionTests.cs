using StreamingAutomation.Domain.Common;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;
using StreamingAutomation.Domain.Exceptions;

namespace StreamingAutomation.UnitTests.Domain;

public class PlaybackSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    private static PlaybackSession NewSession(int duration = 60) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, duration, T0);

    [Fact]
    public void HappyPath_ReachesCompleted_WithTimestampsAndFullProgress()
    {
        var s = NewSession();
        s.Start(T0.AddSeconds(1));
        s.BeginPlayback(T0.AddSeconds(2));
        s.RecordSegment(30, 1024, T0.AddSeconds(3));
        s.EnterBuffering(T0.AddSeconds(4));
        s.ResumePlayback(T0.AddSeconds(5));
        s.Complete(T0.AddSeconds(6));

        Assert.Equal(SessionStatus.Completed, s.Status);
        Assert.Equal(100, s.ProgressPercent);
        Assert.Equal(1024, s.BytesReceived);
        Assert.Equal(1, s.SegmentsReceived);
        Assert.Equal(T0.AddSeconds(1), s.StartedAt);
        Assert.Equal(T0.AddSeconds(2), s.PlaybackStartedAt);
        Assert.Equal(T0.AddSeconds(6), s.EndedAt);
    }

    [Fact]
    public void RecordSegment_UpdatesProgress_AndNeverMovesBackwards()
    {
        var s = NewSession(100);
        s.Start(T0);
        s.BeginPlayback(T0);
        s.RecordSegment(50, 10, T0);
        s.RecordSegment(20, 10, T0);

        Assert.Equal(50, s.ProgressPercent);
        Assert.Equal(20, s.BytesReceived);
    }

    [Fact]
    public void Fail_StoresErrorDetails()
    {
        var s = NewSession();
        s.Start(T0);
        s.Fail("MEDIA_HTTP_503", "Service unavailable", T0.AddSeconds(1));

        Assert.Equal(SessionStatus.Failed, s.Status);
        Assert.Equal("MEDIA_HTTP_503", s.ErrorCode);
        Assert.Equal(T0.AddSeconds(1), s.EndedAt);
    }

    [Fact]
    public void CompletingAPendingSession_Throws()
    {
        var s = NewSession();
        var ex = Assert.Throws<InvalidStateTransitionException>(() => s.Complete(T0));
        Assert.Equal("Pending", ex.From);
        Assert.Equal("Completed", ex.To);
    }

    [Fact]
    public void TerminalSession_CannotBeRestarted()
    {
        var s = NewSession();
        s.Cancel(T0);
        Assert.Throws<InvalidStateTransitionException>(() => s.Start(T0));
    }

    [Fact]
    public void RecordSegment_WhenNotPlaying_Throws()
    {
        var s = NewSession();
        s.Start(T0);
        Assert.Throws<DomainException>(() => s.RecordSegment(1, 1, T0));
    }

    [Theory]
    [InlineData(SessionStatus.Pending, SessionStatus.Starting, true)]
    [InlineData(SessionStatus.Starting, SessionStatus.Playing, true)]
    [InlineData(SessionStatus.Playing, SessionStatus.Buffering, true)]
    [InlineData(SessionStatus.Buffering, SessionStatus.Playing, true)]
    [InlineData(SessionStatus.Playing, SessionStatus.Completed, true)]
    [InlineData(SessionStatus.Pending, SessionStatus.Playing, false)]
    [InlineData(SessionStatus.Buffering, SessionStatus.Completed, false)]
    [InlineData(SessionStatus.Completed, SessionStatus.Failed, false)]
    [InlineData(SessionStatus.Failed, SessionStatus.Playing, false)]
    public void StateMachine_Table(SessionStatus from, SessionStatus to, bool expected) =>
        Assert.Equal(expected, SessionStateMachine.CanTransition(from, to));

    [Fact]
    public void EveryNonTerminalStatus_CanBeInterrupted()
    {
        foreach (var status in Enum.GetValues<SessionStatus>().Where(x => !x.IsTerminal()))
        {
            Assert.True(SessionStateMachine.CanTransition(status, SessionStatus.Interrupted), status.ToString());
        }
    }
}

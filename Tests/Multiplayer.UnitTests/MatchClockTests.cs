using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace TruyTimDanChu.Tests;

public sealed class MatchClockTests
{
    [Fact]
    public void BeforeDeadline_IsAccepting()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var clock = new MatchClock(TimeSpan.FromSeconds(10), time);
        clock.Start();

        time.Advance(TimeSpan.FromSeconds(9.999));

        Assert.True(clock.IsBeforeDeadline());
        Assert.Equal(9_999, clock.GetElapsedMilliseconds());
    }

    [Fact]
    public void AtDeadline_IsNotAccepting()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var clock = new MatchClock(TimeSpan.FromSeconds(10), time);
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.False(clock.IsBeforeDeadline());
        Assert.Equal(0, clock.GetRemainingMilliseconds());
    }

    [Fact]
    public void Finish_IsIdempotent_AndUsesServerUtc()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var clock = new MatchClock(TimeSpan.FromSeconds(10), time);
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.True(clock.TryFinish(MatchEndReason.Timeout, out var first));
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(clock.TryFinish(MatchEndReason.AdminEnded, out var second));
        Assert.Equal(first, second);
        Assert.Equal(MatchEndReason.Timeout, clock.EndReason);
    }

    [Fact]
    public void SuppliedStartUtc_IsPreserved_InSnapshot()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var supplied = DateTimeOffset.Parse("2026-01-01T00:01:00Z");
        var clock = new MatchClock(TimeSpan.FromMinutes(5), time);
        clock.Start(supplied);

        var snapshot = clock.GetSnapshot("match-1");

        Assert.Equal(supplied, snapshot.StartedAtUtc);
        Assert.Equal(supplied.AddMinutes(5), snapshot.DeadlineUtc);
        Assert.Equal(300_000, snapshot.RemainingMilliseconds);
    }

    [Fact]
    public void Clock_DoesNotUseUtcWallClockForElapsed()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var clock = new MatchClock(TimeSpan.FromSeconds(10), time);
        clock.Start();
        time.SetUtc(DateTimeOffset.Parse("2030-01-01T00:00:00Z"));

        Assert.Equal(0, clock.GetElapsedMilliseconds());
        Assert.True(clock.IsBeforeDeadline());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan value)
        {
            _utcNow += value;
            _timestamp += (long)(value.TotalSeconds * TimestampFrequency);
        }

        public void SetUtc(DateTimeOffset value) => _utcNow = value;
    }
}

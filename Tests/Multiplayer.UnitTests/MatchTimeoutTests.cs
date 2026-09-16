using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class MatchTimeoutTests
{
    [Fact]
    public void WorkerEntryPoint_FinishesRoomOnce_AndMarksUnfinishedTeamsTimedOut()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var manager = new RoomManager(Options.Create(new RoomServerOptions { MinTimeLimitSeconds = 60 }), timeProvider: time);
        var created = manager.CreateRoom(new CreateRoomRequest("Timeout", 60));
        Assert.True(created.Success, created.ErrorCode);
        var room = manager.GetRoomById(created.RoomId!)!;
        room.UtcNowProvider = time.GetUtcNow;
        var team = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, created.AdminToken!, "Đỏ", "#E53935", 2));
        var otherTeam = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, created.AdminToken!, "Xanh", "#1E88E5", 2));
        Assert.True(team.Success);
        Assert.True(otherTeam.Success);
        var player = room.TryAddPlayer("Alice", "conn", 10).Player!;
        var otherPlayer = room.TryAddPlayer("Bruno", "conn-2", 10).Player!;
        Assert.True(manager.JoinTeam(new JoinTeamRequest(room.RoomId, player.PlayerId, team.Team!.TeamId)).Response.Success);
        Assert.True(manager.JoinTeam(new JoinTeamRequest(room.RoomId, otherPlayer.PlayerId, otherTeam.Team!.TeamId)).Response.Success);
        Assert.True(manager.SetReady(new SetReadyRequest(room.RoomId, player.PlayerId, true)).Success);
        Assert.True(manager.SetReady(new SetReadyRequest(room.RoomId, otherPlayer.PlayerId, true)).Success);
        var started = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, created.AdminToken!, 1));
        Assert.True(started.Success, started.ErrorCode);
        Assert.True(room.TryForceAdvanceToPlaying(started.MatchId!));

        var events = 0;
        room.OnMatchFinished = (_, _) => events++;
        time.Advance(TimeSpan.FromSeconds(60));

        Assert.True(room.TryExpireMatch(out var first));
        Assert.NotNull(first);
        Assert.Equal(RoomStatus.Finished, room.Status);
        Assert.Equal(MatchEndReason.Timeout, room.MatchEndReason);
        Assert.All(room.Results!.Teams, result =>
        {
            Assert.Equal(TeamResultStatus.TimedOut, result.Status);
            Assert.Null(result.Rank);
        });
        Assert.False(room.TryExpireMatch(out var second));
        Assert.Null(second);
        Assert.Equal(1, events);
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
    }
}

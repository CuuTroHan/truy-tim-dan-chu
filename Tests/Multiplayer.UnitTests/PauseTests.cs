using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class PauseTests
{
    [Fact]
    public void PauseFreezesElapsed_AndResumeExcludesPausedDuration()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var (manager, room, token, matchId) = CreatePlayingRoom(time);
        time.Advance(TimeSpan.FromSeconds(10));
        var before = room.Clock!.GetElapsedMilliseconds();

        var paused = manager.AdminPauseMatch(new AdminPauseMatchRequest(room.RoomId, token, matchId, "pause-1"), "admin");
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(paused.Success);
        Assert.Equal(RoomStatus.Paused, room.Status);
        Assert.Equal(before, room.Clock.GetElapsedMilliseconds());
        Assert.False(room.IsMatchAcceptingCommands());

        var resumed = manager.AdminResumeMatch(new AdminResumeMatchRequest(room.RoomId, token, matchId, "resume-1"), "admin");
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(resumed.Success);
        Assert.Equal(RoomStatus.Playing, room.Status);
        Assert.InRange(room.Clock.GetElapsedMilliseconds(), 14_999, 15_001);
        Assert.Equal(30_000, room.Clock.TotalPausedMilliseconds);
        Assert.Equal(30_000, resumed.Event!.TotalPausedMilliseconds);
    }

    [Fact]
    public void PauseAndResumeCommandsAreIdempotent_AndRequireCurrentMatch()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var (manager, room, token, matchId) = CreatePlayingRoom(time);
        var first = manager.AdminPauseMatch(new AdminPauseMatchRequest(room.RoomId, token, matchId, "pause-1"), "admin");
        var replay = manager.AdminPauseMatch(new AdminPauseMatchRequest(room.RoomId, token, matchId, "pause-1"), "admin");
        var wrong = manager.AdminResumeMatch(new AdminResumeMatchRequest(room.RoomId, token, "old-match", "resume"), "admin");

        Assert.True(first.Success);
        Assert.True(replay.Success);
        Assert.Equal(first.Event, replay.Event);
        Assert.False(wrong.Success);
        Assert.Equal(RoomErrorCodes.MatchIdMismatch, wrong.ErrorCode);
    }

    private static (RoomManager Manager, RoomInstance Room, string Token, string MatchId) CreatePlayingRoom(ManualTimeProvider time)
    {
        var manager = new RoomManager(Options.Create(new RoomServerOptions { MinTimeLimitSeconds = 60 }), timeProvider: time);
        var created = manager.CreateRoom(new CreateRoomRequest("Pause", 60), "admin");
        var room = manager.GetRoomById(created.RoomId!)!;
        room.UtcNowProvider = time.GetUtcNow;
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, created.AdminToken!, "Red", "#E53935", 2)).Team!;
        var blue = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, created.AdminToken!, "Blue", "#1E88E5", 2)).Team!;
        var a = room.TryAddPlayer("Alice", "a", 10).Player!;
        var b = room.TryAddPlayer("Bruno", "b", 10).Player!;
        Assert.True(manager.JoinTeam(new JoinTeamRequest(room.RoomId, a.PlayerId, red.TeamId)).Response.Success);
        Assert.True(manager.JoinTeam(new JoinTeamRequest(room.RoomId, b.PlayerId, blue.TeamId)).Response.Success);
        Assert.True(manager.SetReady(new SetReadyRequest(room.RoomId, a.PlayerId, true)).Success);
        Assert.True(manager.SetReady(new SetReadyRequest(room.RoomId, b.PlayerId, true)).Success);
        var started = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, created.AdminToken!, 1));
        Assert.True(started.Success);
        Assert.True(room.TryForceAdvanceToPlaying(started.MatchId!));
        return (manager, room, created.AdminToken!, started.MatchId!);
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

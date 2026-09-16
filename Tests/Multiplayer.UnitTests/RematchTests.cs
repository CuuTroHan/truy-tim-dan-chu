using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class RematchTests
{
    [Fact]
    public void NewMatch_ResetsLobbyState_AndPreservesRosterConfiguration()
    {
        var (manager, room, token, adminConnection, matchId, red, blue) = CreateFinishedRoom();
        var oldVersion = room.Version;

        var response = manager.AdminNewMatch(new AdminNewMatchRequest(room.RoomId, token, "new-match-1"), adminConnection);

        Assert.True(response.Success, response.ErrorCode);
        Assert.Equal(RoomStatus.Lobby, room.Status);
        Assert.Null(room.MatchId);
        Assert.Null(room.Clock);
        Assert.True(room.Version > oldVersion);
        Assert.Equal(red.Name, response.Teams!.Single(t => t.TeamId == red.TeamId).Name);
        Assert.Equal(blue.Color, response.Teams!.Single(t => t.TeamId == blue.TeamId).Color);
        Assert.All(response.Players!, player => Assert.False(player.IsReady));
        Assert.All(room.GetTeamSnapshots(), team => Assert.Null(team.FinishedAtUtc));
    }

    [Fact]
    public void NewMatch_IsIdempotent_AndRequiresAdminBinding()
    {
        var (manager, room, token, adminConnection, _, _, _) = CreateFinishedRoom();
        var request = new AdminNewMatchRequest(room.RoomId, token, "new-match-1");

        var rejected = manager.AdminNewMatch(request, "old-admin-connection");
        Assert.False(rejected.Success);
        Assert.Equal(RoomErrorCodes.AdminNotBound, rejected.ErrorCode);

        var first = manager.AdminNewMatch(request, adminConnection);
        var replay = manager.AdminNewMatch(request, adminConnection);
        Assert.True(first.Success);
        Assert.True(replay.Success);
        Assert.Equal(first.Room, replay.Room);
        Assert.Equal(RoomStatus.Lobby, room.Status);
    }

    [Fact]
    public void NewMatchOnlyAllowedAfterFinished()
    {
        var manager = new RoomManager(Options.Create(new RoomServerOptions { MinTimeLimitSeconds = 60 }));
        var created = manager.CreateRoom(new CreateRoomRequest("Rematch", 60), "admin");
        var response = manager.AdminNewMatch(new AdminNewMatchRequest(created.RoomId!, created.AdminToken!, "cmd"), "admin");
        Assert.False(response.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, response.ErrorCode);
    }

    private static (RoomManager Manager, RoomInstance Room, string Token, string AdminConnection,
        string MatchId, TeamSnapshot Red, TeamSnapshot Blue) CreateFinishedRoom()
    {
        var manager = new RoomManager(Options.Create(new RoomServerOptions { MinTimeLimitSeconds = 60 }));
        var created = manager.CreateRoom(new CreateRoomRequest("Rematch", 60), "admin-connection");
        var room = manager.GetRoomById(created.RoomId!)!;
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
        Assert.True(room.TryMarkTeamFinished(red.TeamId, DateTimeOffset.UtcNow, out _));
        Assert.True(room.TryMarkTeamFinished(blue.TeamId, DateTimeOffset.UtcNow, out _));
        Assert.True(room.TryFinalizeIfAllTeamsFinished(out _));
        Assert.Equal(RoomStatus.Finished, room.Status);
        return (manager, room, created.AdminToken!, "admin-connection", started.MatchId!, red, blue);
    }
}

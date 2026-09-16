using System.Text.Json;
using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class PublicProgressTests
{
    [Fact]
    public void Projection_ContainsOnlyAllowlistedTeamFields()
    {
        var snapshot = new PublicProgressSnapshot("match-1", 7, true,
            [new PublicTeamProgressSnapshot("red", "Đỏ", "#E53935", 2, PublicTeamStatus.Playing, 1)]);

        var json = JsonSerializer.Serialize(snapshot);

        Assert.Contains("TeamId", json);
        Assert.Contains("Shards", json);
        Assert.DoesNotContain("ObjectId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Position", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reservation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoomProjection_RespectsVisibilityPolicy()
    {
        var visible = CreatePlayingRoom(showLeaderboard: true);
        var publicSnapshot = visible.Room.GetPublicProgressSnapshot();
        Assert.NotNull(publicSnapshot);
        Assert.True(publicSnapshot!.IsVisible);
        Assert.Equal(2, publicSnapshot.Teams.Count);
        Assert.All(publicSnapshot.Teams, team => Assert.Equal(PublicTeamStatus.Playing, team.Status));

        var hidden = CreatePlayingRoom(showLeaderboard: false);
        var hiddenSnapshot = hidden.Room.GetPublicProgressSnapshot();
        Assert.NotNull(hiddenSnapshot);
        Assert.False(hiddenSnapshot!.IsVisible);
        Assert.Empty(hiddenSnapshot.Teams);
    }

    private static (RoomManager Manager, RoomInstance Room) CreatePlayingRoom(bool showLeaderboard)
    {
        var manager = new RoomManager(Options.Create(new RoomServerOptions { MinTimeLimitSeconds = 60 }));
        var created = manager.CreateRoom(new CreateRoomRequest("Public", 60, true, false, showLeaderboard));
        Assert.True(created.Success, created.ErrorCode);
        var room = manager.GetRoomById(created.RoomId!)!;
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, created.AdminToken!, "Đỏ", "#E53935", 2));
        var blue = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, created.AdminToken!, "Xanh", "#1E88E5", 2));
        Assert.True(red.Success);
        Assert.True(blue.Success);
        var a = room.TryAddPlayer("Alice", "a", 10).Player!;
        var b = room.TryAddPlayer("Bruno", "b", 10).Player!;
        Assert.True(manager.JoinTeam(new JoinTeamRequest(room.RoomId, a.PlayerId, red.Team!.TeamId)).Response.Success);
        Assert.True(manager.JoinTeam(new JoinTeamRequest(room.RoomId, b.PlayerId, blue.Team!.TeamId)).Response.Success);
        Assert.True(manager.SetReady(new SetReadyRequest(room.RoomId, a.PlayerId, true)).Success);
        Assert.True(manager.SetReady(new SetReadyRequest(room.RoomId, b.PlayerId, true)).Success);
        var started = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, created.AdminToken!, 1));
        Assert.True(started.Success, started.ErrorCode);
        Assert.True(room.TryForceAdvanceToPlaying(started.MatchId!));
        return (manager, room);
    }
}

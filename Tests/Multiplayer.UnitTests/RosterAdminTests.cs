using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class RosterAdminTests
{
    private static (RoomManager Manager, string RoomId, string AdminToken) CreateRoom(bool allowSelfTeamSelection = true)
    {
        var manager = new RoomManager();
        var res = manager.CreateRoom(new CreateRoomRequest(
            "Phòng Phân Đội",
            900,
            AllowSelfTeamSelection: allowSelfTeamSelection
        ));
        Assert.True(res.Success);
        return (manager, res.RoomId!, res.AdminToken!);
    }

    [Fact]
    public void AdminMovePlayer_ValidTeam_Succeeds()
    {
        var (manager, roomId, token) = CreateRoom(allowSelfTeamSelection: false);
        var room = manager.GetRoomById(roomId)!;

        // Admin adds Team 1 (Cap 2)
        var teamRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Đội Đỏ", "#E53935", 2));
        Assert.True(teamRes.Success);
        var teamId = teamRes.Team!.TeamId;

        // Player joins room (self-selection disabled)
        var joinRes = room.TryAddPlayer("Minh Quân", "conn-1", 40);
        Assert.True(joinRes.Success);
        var playerId = joinRes.Player!.PlayerId;

        // Player cannot self-join
        var selfJoin = room.TryJoinTeam(playerId, teamId, enforceSelfSelection: true);
        Assert.False(selfJoin.Success);
        Assert.Equal(RoomErrorCodes.SelfSelectionDisabled, selfJoin.ErrorCode);

        // Admin moves player to Team 1
        var moveRes = manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, playerId, teamId));
        Assert.True(moveRes.Success);
        Assert.Equal(teamId, moveRes.NewTeamId);
        Assert.Null(moveRes.OldTeamId);

        var player = room.GetPlayer(playerId)!;
        Assert.Equal(teamId, player.TeamId);
    }

    [Fact]
    public void AdminMovePlayer_FullTeam_FailsAndPreservesOldTeam()
    {
        var (manager, roomId, token) = CreateRoom();
        var room = manager.GetRoomById(roomId)!;

        // Team 1 (Cap 1), Team 2 (Cap 1)
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Đỏ", "#E53935", 1)).Team!;
        var blue = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Xanh", "#1E88E5", 1)).Team!;

        var p1 = room.TryAddPlayer("P1", "c1", 40).Player!;
        var p2 = room.TryAddPlayer("P2", "c2", 40).Player!;

        // P1 in Red (1/1), P2 in Blue (1/1)
        manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, p1.PlayerId, red.TeamId));
        manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, p2.PlayerId, blue.TeamId));

        Assert.Equal(red.TeamId, p1.TeamId);
        Assert.Equal(blue.TeamId, p2.TeamId);

        // Admin tries to move P2 to Red (which is full)
        var moveRes = manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, p2.PlayerId, red.TeamId));
        Assert.False(moveRes.Success);
        Assert.Equal(RoomErrorCodes.TeamFull, moveRes.ErrorCode);

        // P2 must remain in Blue (atomic switch preserved)
        Assert.Equal(blue.TeamId, p2.TeamId);
    }

    [Fact]
    public void AdminMovePlayer_RemoveFromTeam_Succeeds()
    {
        var (manager, roomId, token) = CreateRoom();
        var room = manager.GetRoomById(roomId)!;

        var red = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Đỏ", "#E53935", 2)).Team!;
        var p = room.TryAddPlayer("P1", "c1", 40).Player!;

        manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, p.PlayerId, red.TeamId));
        Assert.Equal(red.TeamId, p.TeamId);

        // Move to null (unassigned)
        var moveRes = manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, p.PlayerId, null));
        Assert.True(moveRes.Success);
        Assert.Null(moveRes.NewTeamId);
        Assert.Equal(red.TeamId, moveRes.OldTeamId);
        Assert.Null(p.TeamId);
    }

    [Fact]
    public void AdminMovePlayer_UnauthorizedAdmin_Fails()
    {
        var (manager, roomId, token) = CreateRoom();
        var room = manager.GetRoomById(roomId)!;
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Đỏ", "#E53935", 2)).Team!;
        var p = room.TryAddPlayer("P1", "c1", 40).Player!;

        var moveRes = manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, "wrong-token", p.PlayerId, red.TeamId));
        Assert.False(moveRes.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, moveRes.ErrorCode);
    }

    [Fact]
    public void AdminMovePlayer_PlayerOrTeamNotFound_Fails()
    {
        var (manager, roomId, token) = CreateRoom();
        var room = manager.GetRoomById(roomId)!;
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Đỏ", "#E53935", 2)).Team!;

        // Nonexistent player
        var moveRes1 = manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, "unknown-player", red.TeamId));
        Assert.False(moveRes1.Success);
        Assert.Equal(RoomErrorCodes.PlayerNotFound, moveRes1.ErrorCode);

        // Nonexistent team
        var p = room.TryAddPlayer("P1", "c1", 40).Player!;
        var moveRes2 = manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, p.PlayerId, "unknown-team"));
        Assert.False(moveRes2.Success);
        Assert.Equal(RoomErrorCodes.TeamNotFound, moveRes2.ErrorCode);
    }

    [Fact]
    public void AdminKickPlayer_InTeam_RemovesFromRoomAndReleasesSlot()
    {
        var (manager, roomId, token) = CreateRoom();
        var room = manager.GetRoomById(roomId)!;

        var red = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Đỏ", "#E53935", 1)).Team!;
        var p1 = room.TryAddPlayer("P1", "c1", 40).Player!;
        manager.AdminMovePlayer(new AdminMovePlayerRequest(roomId, token, p1.PlayerId, red.TeamId));

        Assert.Equal(1, room.PlayerCount);
        Assert.Equal(1, room.GetPlayerSnapshots().Count(p => p.TeamId == red.TeamId));

        // Kick P1
        var (kickRes, oldTeamId, connId) = manager.AdminKickPlayer(new AdminKickPlayerRequest(roomId, token, p1.PlayerId, "Vi phạm quy chế"));
        Assert.True(kickRes.Success);
        Assert.Equal(p1.PlayerId, kickRes.KickedPlayerId);
        Assert.Equal(red.TeamId, oldTeamId);
        Assert.Equal("c1", connId);

        // Room has 0 players, Red team is empty (0/1)
        Assert.Equal(0, room.PlayerCount);
        Assert.Null(room.GetPlayer(p1.PlayerId));
        Assert.Equal(0, room.GetPlayerSnapshots().Count(p => p.TeamId == red.TeamId));

        // Now another player can join Red
        var p2 = room.TryAddPlayer("P2", "c2", 40).Player!;
        var joinRed = room.TryJoinTeam(p2.PlayerId, red.TeamId, enforceSelfSelection: true);
        Assert.True(joinRed.Success);
        Assert.Equal(red.TeamId, p2.TeamId);
    }

    [Fact]
    public void AdminKickPlayer_UnauthorizedAdmin_Fails()
    {
        var (manager, roomId, token) = CreateRoom();
        var room = manager.GetRoomById(roomId)!;
        var p1 = room.TryAddPlayer("P1", "c1", 40).Player!;

        var (kickRes, _, _) = manager.AdminKickPlayer(new AdminKickPlayerRequest(roomId, "fake-token", p1.PlayerId));
        Assert.False(kickRes.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, kickRes.ErrorCode);
        Assert.Equal(1, room.PlayerCount);
    }

    [Fact]
    public void AdminToggleSelfTeamSelection_EnforcesPolicy()
    {
        var (manager, roomId, token) = CreateRoom(allowSelfTeamSelection: true);
        var room = manager.GetRoomById(roomId)!;
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Đỏ", "#E53935", 2)).Team!;
        var p = room.TryAddPlayer("P1", "c1", 40).Player!;

        // Initially true
        Assert.True(room.AllowSelfTeamSelection);

        // Turn off
        var toggleOff = manager.AdminToggleSelfTeamSelection(new AdminToggleSelfTeamSelectionRequest(roomId, token, false));
        Assert.True(toggleOff.Success);
        Assert.False(toggleOff.AllowSelfTeamSelection);
        Assert.False(room.AllowSelfTeamSelection);

        // Player cannot self-join
        var failJoin = room.TryJoinTeam(p.PlayerId, red.TeamId, enforceSelfSelection: true);
        Assert.False(failJoin.Success);
        Assert.Equal(RoomErrorCodes.SelfSelectionDisabled, failJoin.ErrorCode);

        // Turn on
        var toggleOn = manager.AdminToggleSelfTeamSelection(new AdminToggleSelfTeamSelectionRequest(roomId, token, true));
        Assert.True(toggleOn.Success);
        Assert.True(toggleOn.AllowSelfTeamSelection);
        Assert.True(room.AllowSelfTeamSelection);

        // Player can now self-join
        var successJoin = room.TryJoinTeam(p.PlayerId, red.TeamId, enforceSelfSelection: true);
        Assert.True(successJoin.Success);
        Assert.Equal(red.TeamId, p.TeamId);
    }
}


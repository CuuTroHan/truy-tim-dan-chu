using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class ReadinessTests
{
    private static (RoomManager Manager, RoomInstance Room, string AdminToken) CreateRoom()
    {
        var manager = new RoomManager();
        var res = manager.CreateRoom(new CreateRoomRequest("Phòng Sẵn Sàng", 900));
        Assert.True(res.Success);
        var room = manager.GetRoomById(res.RoomId!)!;
        return (manager, room, res.AdminToken!);
    }

    private static (PlayerSnapshot Player, string ConnId) AddPlayer(RoomInstance room, string name, string connId)
    {
        var joinRes = room.TryAddPlayer(name, connId, 40);
        Assert.True(joinRes.Success);
        return (joinRes.Player!.GetSnapshot(), connId);
    }

    [Fact]
    public void SetReady_WithoutTeam_FailsWithPlayerNotInTeam()
    {
        var (manager, room, _) = CreateRoom();
        var (player, _) = AddPlayer(room, "Minh Quân", "conn-1");

        var res = manager.SetReady(new SetReadyRequest(room.RoomId, player.PlayerId, true));

        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.PlayerNotInTeam, res.ErrorCode);
        Assert.False(res.IsReady);

        var snapshot = room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId);
        Assert.False(snapshot.IsReady);
    }

    [Fact]
    public void SetReady_WithTeam_TogglesSuccessfully()
    {
        var (manager, room, token) = CreateRoom();
        var teamRes = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Đỏ", "#E53935", 4));
        var teamId = teamRes.Team!.TeamId;

        var (player, _) = AddPlayer(room, "Minh Quân", "conn-1");
        var joinTeamRes = room.TryJoinTeam(player.PlayerId, teamId);
        Assert.True(joinTeamRes.Success);

        // Toggle Ready ON
        var readyRes = manager.SetReady(new SetReadyRequest(room.RoomId, player.PlayerId, true));
        Assert.True(readyRes.Success);
        Assert.True(readyRes.IsReady);
        Assert.True(room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId).IsReady);

        // Toggle Ready OFF
        var unreadyRes = manager.SetReady(new SetReadyRequest(room.RoomId, player.PlayerId, false));
        Assert.True(unreadyRes.Success);
        Assert.False(unreadyRes.IsReady);
        Assert.False(room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId).IsReady);
    }

    [Fact]
    public void TeamChange_OrLeave_ResetsPlayerReady()
    {
        var (manager, room, token) = CreateRoom();
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Đỏ", "#E53935", 4)).Team!.TeamId;
        var blue = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Xanh", "#1E88E5", 4)).Team!.TeamId;

        var (player, _) = AddPlayer(room, "Minh Quân", "conn-1");
        room.TryJoinTeam(player.PlayerId, red);
        manager.SetReady(new SetReadyRequest(room.RoomId, player.PlayerId, true));
        Assert.True(room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId).IsReady);

        // Switch to Blue -> Ready resets to false
        room.TryJoinTeam(player.PlayerId, blue);
        Assert.False(room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId).IsReady);

        // Ready up again, then leave -> Ready resets
        manager.SetReady(new SetReadyRequest(room.RoomId, player.PlayerId, true));
        Assert.True(room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId).IsReady);
        room.TryLeaveTeam(player.PlayerId);
        Assert.False(room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId).IsReady);
    }

    [Fact]
    public void AdminMovePlayer_ResetsPlayerReady()
    {
        var (manager, room, token) = CreateRoom();
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Đỏ", "#E53935", 4)).Team!.TeamId;
        var blue = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Xanh", "#1E88E5", 4)).Team!.TeamId;

        var (player, _) = AddPlayer(room, "Minh Quân", "conn-1");
        room.TryJoinTeam(player.PlayerId, red);
        manager.SetReady(new SetReadyRequest(room.RoomId, player.PlayerId, true));

        // Admin moves player to Blue
        manager.AdminMovePlayer(new AdminMovePlayerRequest(room.RoomId, token, player.PlayerId, blue));
        Assert.False(room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId).IsReady);
    }

    [Fact]
    public void TeamConfigChange_ResetsAllPlayersReady()
    {
        var (manager, room, token) = CreateRoom();
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Đỏ", "#E53935", 4)).Team!;

        var (p1, _) = AddPlayer(room, "Alice", "conn-1");
        var (p2, _) = AddPlayer(room, "Bob", "conn-2");
        room.TryJoinTeam(p1.PlayerId, red.TeamId);
        room.TryJoinTeam(p2.PlayerId, red.TeamId);

        manager.SetReady(new SetReadyRequest(room.RoomId, p1.PlayerId, true));
        manager.SetReady(new SetReadyRequest(room.RoomId, p2.PlayerId, true));
        Assert.All(room.GetPlayerSnapshots(), p => Assert.True(p.IsReady));

        // Admin updates capacity of Red -> all ready reset
        manager.AdminUpdateTeam(new AdminUpdateTeamRequest(room.RoomId, token, red.TeamId, "Đội Đỏ Mới", red.Color, 5));
        Assert.All(room.GetPlayerSnapshots(), p => Assert.False(p.IsReady));

        // Ready up again, then toggle self selection -> ready reset
        manager.SetReady(new SetReadyRequest(room.RoomId, p1.PlayerId, true));
        manager.SetReady(new SetReadyRequest(room.RoomId, p2.PlayerId, true));
        manager.AdminToggleSelfTeamSelection(new AdminToggleSelfTeamSelectionRequest(room.RoomId, token, false));
        Assert.All(room.GetPlayerSnapshots(), p => Assert.False(p.IsReady));
    }

    [Fact]
    public void JoinLock_PreventsNewJoins_PreservesExisting()
    {
        var (manager, room, token) = CreateRoom();
        var (existingPlayer, _) = AddPlayer(room, "Alice", "conn-1");

        // Admin locks join
        var lockRes = manager.AdminSetJoinLock(new AdminSetJoinLockRequest(room.RoomId, token, true));
        Assert.True(lockRes.Success);
        Assert.True(lockRes.IsJoinLocked);
        Assert.True(room.IsJoinLocked);
        Assert.True(room.GetSnapshot().IsJoinLocked);

        // Existing player still exists
        Assert.Equal(1, room.PlayerCount);
        Assert.NotNull(room.GetPlayer(existingPlayer.PlayerId));

        // New player tries to join -> rejected
        var newJoin = room.TryAddPlayer("Bob", "conn-2", 40);
        Assert.False(newJoin.Success);
        Assert.Equal(RoomErrorCodes.JoinLocked, newJoin.ErrorCode);

        // Admin unlocks join
        var unlockRes = manager.AdminSetJoinLock(new AdminSetJoinLockRequest(room.RoomId, token, false));
        Assert.True(unlockRes.Success);
        Assert.False(unlockRes.IsJoinLocked);

        // Now Bob can join
        var bobJoin = room.TryAddPlayer("Bob", "conn-2", 40);
        Assert.True(bobJoin.Success);
        Assert.Equal(2, room.PlayerCount);
    }

    [Fact]
    public void RosterLock_BlocksSelfTeamChange_AllowsAdminMove()
    {
        var (manager, room, token) = CreateRoom();
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Đỏ", "#E53935", 4)).Team!.TeamId;
        var blue = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Xanh", "#1E88E5", 4)).Team!.TeamId;

        var (player, _) = AddPlayer(room, "Minh Quân", "conn-1");
        room.TryJoinTeam(player.PlayerId, red);

        // Lock roster
        var lockRes = manager.AdminSetRosterLock(new AdminSetRosterLockRequest(room.RoomId, token, true));
        Assert.True(lockRes.Success);
        Assert.True(lockRes.IsRosterLocked);
        Assert.True(room.GetSnapshot().IsRosterLocked);

        // Player tries to join Blue -> rejected
        var joinBlue = room.TryJoinTeam(player.PlayerId, blue, enforceSelfSelection: true);
        Assert.False(joinBlue.Success);
        Assert.Equal(RoomErrorCodes.RosterLocked, joinBlue.ErrorCode);

        // Player tries to leave Red -> rejected
        var leave = room.TryLeaveTeam(player.PlayerId, enforceSelfSelection: true);
        Assert.False(leave.Success);
        Assert.Equal(RoomErrorCodes.RosterLocked, leave.ErrorCode);

        // Admin moves player -> allowed
        var adminMove = manager.AdminMovePlayer(new AdminMovePlayerRequest(room.RoomId, token, player.PlayerId, blue));
        Assert.True(adminMove.Success);
        Assert.Equal(blue, room.GetPlayer(player.PlayerId)?.TeamId);
    }

    [Fact]
    public void Lock_UnauthorizedToken_Fails()
    {
        var (manager, room, _) = CreateRoom();

        var joinLockRes = manager.AdminSetJoinLock(new AdminSetJoinLockRequest(room.RoomId, "fake-token", true));
        Assert.False(joinLockRes.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, joinLockRes.ErrorCode);

        var rosterLockRes = manager.AdminSetRosterLock(new AdminSetRosterLockRequest(room.RoomId, "fake-token", true));
        Assert.False(rosterLockRes.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, rosterLockRes.ErrorCode);
    }

    [Fact]
    public void ReadinessValidator_ValidatesBlockersAndWarnings()
    {
        var (manager, room, token) = CreateRoom();

        // 0. Empty room
        var v0 = ReadinessValidator.Validate(room.GetSnapshot(), [], []);
        Assert.False(v0.CanStart);
        Assert.Contains(v0.BlockingReasons, r => r.Contains("Chưa có đội thi đấu"));

        // 1. Add 1 team only
        var red = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đỏ", "#E53935", 4)).Team!;
        var v1 = ReadinessValidator.Validate(room.GetSnapshot(), [], room.GetTeamSnapshots());
        Assert.False(v1.CanStart);
        Assert.Contains(v1.BlockingReasons, r => r.Contains("ít nhất 2 đội"));

        // 2. Add second team, but no players
        var blue = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Xanh", "#1E88E5", 4)).Team!;
        var v2 = ReadinessValidator.Validate(room.GetSnapshot(), [], room.GetTeamSnapshots());
        Assert.False(v2.CanStart);
        Assert.Contains(v2.BlockingReasons, r => r.Contains("chưa có người chơi"));

        // 3. Add 2 players, unassigned
        var (p1, _) = AddPlayer(room, "P1", "conn-1");
        var (p2, _) = AddPlayer(room, "P2", "conn-2");
        var v3 = ReadinessValidator.Validate(room.GetSnapshot(), room.GetPlayerSnapshots(), room.GetTeamSnapshots());
        Assert.False(v3.CanStart);
        Assert.Contains(v3.BlockingReasons, r => r.Contains("chưa được phân vào đội"));

        // 4. Assign both to Red (Blue empty, Red not ready)
        room.TryJoinTeam(p1.PlayerId, red.TeamId);
        room.TryJoinTeam(p2.PlayerId, red.TeamId);
        var v4 = ReadinessValidator.Validate(room.GetSnapshot(), room.GetPlayerSnapshots(), room.GetTeamSnapshots());
        Assert.False(v4.CanStart);
        Assert.Contains(v4.BlockingReasons, r => r.Contains("Đội 'Xanh' chưa có thành viên"));
        Assert.Contains(v4.BlockingReasons, r => r.Contains("chưa sẵn sàng"));

        // 5. Move P2 to Blue, both ready
        room.TryJoinTeam(p2.PlayerId, blue.TeamId);
        manager.SetReady(new SetReadyRequest(room.RoomId, p1.PlayerId, true));
        manager.SetReady(new SetReadyRequest(room.RoomId, p2.PlayerId, true));
        var v5 = ReadinessValidator.Validate(room.GetSnapshot(), room.GetPlayerSnapshots(), room.GetTeamSnapshots());
        Assert.True(v5.CanStart);
        Assert.Empty(v5.BlockingReasons);
        Assert.Empty(v5.Warnings);

        // 6. Add P3 to Red (Red: 2, Blue: 1) and ready up -> CanStart with uneven warning
        var (p3, _) = AddPlayer(room, "P3", "conn-3");
        room.TryJoinTeam(p3.PlayerId, red.TeamId);
        manager.SetReady(new SetReadyRequest(room.RoomId, p3.PlayerId, true));
        var v6 = ReadinessValidator.Validate(room.GetSnapshot(), room.GetPlayerSnapshots(), room.GetTeamSnapshots());
        Assert.True(v6.CanStart);
        Assert.Empty(v6.BlockingReasons);
        Assert.Single(v6.Warnings);
        Assert.Contains("không đồng đều", v6.Warnings[0]);
    }
}


using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public class TeamSelectionTests
{
    private static (RoomManager manager, string roomId, string adminToken, string roomCode) CreateTestRoom(
        bool allowSelfSelection = true,
        int maxPlayersPerRoom = 40)
    {
        var options = Options.Create(new RoomServerOptions
        {
            MaxTeamsPerRoom = 10,
            MaxPlayersPerTeam = 8,
            MaxPlayersPerRoom = maxPlayersPerRoom
        });

        var manager = new RoomManager(options);
        var createRes = manager.CreateRoom(new CreateRoomRequest("Phòng Chọn Đội", AllowSelfTeamSelection: allowSelfSelection));
        Assert.True(createRes.Success);
        Assert.NotNull(createRes.RoomId);
        Assert.NotNull(createRes.AdminToken);
        Assert.NotNull(createRes.RoomCode);

        return (manager, createRes.RoomId, createRes.AdminToken, createRes.RoomCode);
    }

    [Fact]
    public void JoinTeam_SelfSelectionDisabled_FailsWithSelfSelectionDisabled()
    {
        var (manager, roomId, adminToken, roomCode) = CreateTestRoom(allowSelfSelection: false);

        var teamRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        Assert.True(teamRes.Success);

        var joinRes = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 1"), "conn1");
        Assert.True(joinRes.Success);

        var (joinTeamRes, _) = manager.JoinTeam(new JoinTeamRequest(roomId, joinRes.PlayerId!, teamRes.Team!.TeamId));
        Assert.False(joinTeamRes.Success);
        Assert.Equal(RoomErrorCodes.SelfSelectionDisabled, joinTeamRes.ErrorCode);
    }

    [Fact]
    public void JoinTeam_ValidAvailableSlot_Succeeds()
    {
        var (manager, roomId, adminToken, roomCode) = CreateTestRoom();

        var teamRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        var teamId = teamRes.Team!.TeamId;

        var joinRes = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 1"), "conn1");
        var playerId = joinRes.PlayerId!;

        var (joinTeamRes, oldTeamId) = manager.JoinTeam(new JoinTeamRequest(roomId, playerId, teamId));
        Assert.True(joinTeamRes.Success);
        Assert.Equal(teamId, joinTeamRes.TeamId);
        Assert.Null(oldTeamId);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        var player = room.GetPlayer(playerId);
        Assert.NotNull(player);
        Assert.Equal(teamId, player.TeamId);

        var teamSnapshots = room.GetTeamSnapshots();
        Assert.Equal(1, teamSnapshots.Single(t => t.TeamId == teamId).MemberCount);
    }

    [Fact]
    public void JoinTeam_IdempotentDuplicateJoin_DoesNotIncreaseOccupancy()
    {
        var (manager, roomId, adminToken, roomCode) = CreateTestRoom();

        var teamRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        var teamId = teamRes.Team!.TeamId;

        var joinRes = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 1"), "conn1");
        var playerId = joinRes.PlayerId!;

        // Join lần 1
        var (res1, _) = manager.JoinTeam(new JoinTeamRequest(roomId, playerId, teamId));
        Assert.True(res1.Success);

        // Join lại lần 2 (trùng đội đang ở)
        var (res2, _) = manager.JoinTeam(new JoinTeamRequest(roomId, playerId, teamId));
        Assert.True(res2.Success);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        var snapshots = room.GetTeamSnapshots();
        Assert.Equal(1, snapshots.Single(t => t.TeamId == teamId).MemberCount);
    }

    [Fact]
    public void JoinTeam_TeamFull_FailsWithTeamFull()
    {
        var (manager, roomId, adminToken, roomCode) = CreateTestRoom();

        // Đội sức chứa đúng 1 người
        var teamRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 1 Chỗ", "#E53935", 1));
        var teamId = teamRes.Team!.TeamId;

        var p1 = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 1"), "conn1");
        var p2 = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 2"), "conn2");

        var (res1, _) = manager.JoinTeam(new JoinTeamRequest(roomId, p1.PlayerId!, teamId));
        Assert.True(res1.Success);

        var (res2, _) = manager.JoinTeam(new JoinTeamRequest(roomId, p2.PlayerId!, teamId));
        Assert.False(res2.Success);
        Assert.Equal(RoomErrorCodes.TeamFull, res2.ErrorCode);
    }

    [Fact]
    public void SwitchTeam_TargetTeamFull_OldTeamMembershipPreserved()
    {
        var (manager, roomId, adminToken, roomCode) = CreateTestRoom();

        var teamRed = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 2));
        var teamBlue = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Xanh", "#1E88E5", 1));

        var idRed = teamRed.Team!.TeamId;
        var idBlue = teamBlue.Team!.TeamId;

        var p1 = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 1"), "conn1");
        var p2 = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 2"), "conn2");

        // Player 1 vào Blue -> Blue đầy (1/1)
        var (j1, _) = manager.JoinTeam(new JoinTeamRequest(roomId, p1.PlayerId!, idBlue));
        Assert.True(j1.Success);

        // Player 2 vào Red -> Red có (1/2)
        var (j2, _) = manager.JoinTeam(new JoinTeamRequest(roomId, p2.PlayerId!, idRed));
        Assert.True(j2.Success);

        // Player 2 cố gắng đổi sang Blue -> Bị từ chối TEAM_FULL
        var (switchRes, _) = manager.JoinTeam(new JoinTeamRequest(roomId, p2.PlayerId!, idBlue));
        Assert.False(switchRes.Success);
        Assert.Equal(RoomErrorCodes.TeamFull, switchRes.ErrorCode);

        // Rất quan trọng: Player 2 VẪN PHẢI ở Đội Đỏ, không bị mồ côi
        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        var player2 = room.GetPlayer(p2.PlayerId);
        Assert.NotNull(player2);
        Assert.Equal(idRed, player2.TeamId);

        var snapshots = room.GetTeamSnapshots();
        Assert.Equal(1, snapshots.Single(t => t.TeamId == idRed).MemberCount);
        Assert.Equal(1, snapshots.Single(t => t.TeamId == idBlue).MemberCount);
    }

    [Fact]
    public void SwitchTeam_TargetTeamAvailable_SwitchesAndUpdatesBothOccupancies()
    {
        var (manager, roomId, adminToken, roomCode) = CreateTestRoom();

        var teamRed = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 2));
        var teamBlue = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Xanh", "#1E88E5", 2));

        var idRed = teamRed.Team!.TeamId;
        var idBlue = teamBlue.Team!.TeamId;

        var p1 = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 1"), "conn1");
        var playerId = p1.PlayerId!;

        // Player 1 vào Red
        var (j1, _) = manager.JoinTeam(new JoinTeamRequest(roomId, playerId, idRed));
        Assert.True(j1.Success);

        // Player 1 đổi sang Blue
        var (j2, oldTeamId) = manager.JoinTeam(new JoinTeamRequest(roomId, playerId, idBlue));
        Assert.True(j2.Success);
        Assert.Equal(idBlue, j2.TeamId);
        Assert.Equal(idRed, oldTeamId);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        Assert.Equal(idBlue, room.GetPlayer(playerId)!.TeamId);

        var snapshots = room.GetTeamSnapshots();
        Assert.Equal(0, snapshots.Single(t => t.TeamId == idRed).MemberCount);
        Assert.Equal(1, snapshots.Single(t => t.TeamId == idBlue).MemberCount);
    }

    [Fact]
    public void LeaveTeam_WhenInTeam_ReleasesSlotAndAllowsNextPlayerToJoin()
    {
        var (manager, roomId, adminToken, roomCode) = CreateTestRoom();

        var teamRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 1 Chỗ", "#E53935", 1));
        var teamId = teamRes.Team!.TeamId;

        var p1 = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 1"), "conn1");
        var p2 = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 2"), "conn2");

        // Player 1 vào -> Đội đầy
        Assert.True(manager.JoinTeam(new JoinTeamRequest(roomId, p1.PlayerId!, teamId)).Response.Success);
        Assert.False(manager.JoinTeam(new JoinTeamRequest(roomId, p2.PlayerId!, teamId)).Response.Success);

        // Player 1 rời đội
        var (leaveRes, oldTeamId) = manager.LeaveTeam(new LeaveTeamRequest(roomId, p1.PlayerId!));
        Assert.True(leaveRes.Success);
        Assert.Equal(teamId, oldTeamId);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        Assert.Null(room.GetPlayer(p1.PlayerId!)!.TeamId);

        // Bây giờ Player 2 vào được!
        var (p2JoinRes, _) = manager.JoinTeam(new JoinTeamRequest(roomId, p2.PlayerId!, teamId));
        Assert.True(p2JoinRes.Success);
        Assert.Equal(teamId, p2JoinRes.TeamId);
    }

    [Fact]
    public void JoinTeam_AlienTeamId_FailsWithTeamNotFound()
    {
        var (manager, roomId, _, roomCode) = CreateTestRoom();
        var p1 = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player 1"), "conn1");

        var (res, _) = manager.JoinTeam(new JoinTeamRequest(roomId, p1.PlayerId!, "alien-team-999"));
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.TeamNotFound, res.ErrorCode);
    }

    [Fact]
    public async Task ConcurrentJoin_RaceForLastSlot_ProtectedByBarrier()
    {
        var (manager, roomId, adminToken, roomCode) = CreateTestRoom();

        var teamRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Tranh Chấp", "#E53935", 1));
        var teamId = teamRes.Team!.TeamId;

        var pA = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player A"), "connA");
        var pB = manager.JoinRoom(new JoinRoomRequest(roomCode, "Player B"), "connB");

        using var barrier = new Barrier(2);
        JoinTeamResponse? resA = null;
        JoinTeamResponse? resB = null;

        var taskA = Task.Run(() =>
        {
            barrier.SignalAndWait();
            resA = manager.JoinTeam(new JoinTeamRequest(roomId, pA.PlayerId!, teamId)).Response;
        });

        var taskB = Task.Run(() =>
        {
            barrier.SignalAndWait();
            resB = manager.JoinTeam(new JoinTeamRequest(roomId, pB.PlayerId!, teamId)).Response;
        });

        await Task.WhenAll(taskA, taskB);

        Assert.NotNull(resA);
        Assert.NotNull(resB);

        int successCount = (resA.Success ? 1 : 0) + (resB.Success ? 1 : 0);
        Assert.Equal(1, successCount);

        var failed = resA.Success ? resB : resA;
        Assert.False(failed.Success);
        Assert.Equal(RoomErrorCodes.TeamFull, failed.ErrorCode);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        var snapshots = room.GetTeamSnapshots();
        Assert.Equal(1, snapshots.Single(t => t.TeamId == teamId).MemberCount);
    }
}


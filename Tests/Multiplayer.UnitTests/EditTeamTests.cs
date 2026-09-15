using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public class EditTeamTests
{
    private static (RoomManager manager, string roomId, string adminToken) CreateTestRoom(
        int maxTeams = 10,
        int maxPlayersPerTeam = 8,
        int maxPlayersPerRoom = 40)
    {
        var options = Options.Create(new RoomServerOptions
        {
            MaxTeamsPerRoom = maxTeams,
            MaxPlayersPerTeam = maxPlayersPerTeam,
            MaxPlayersPerRoom = maxPlayersPerRoom
        });

        var manager = new RoomManager(options);
        var createRes = manager.CreateRoom(new CreateRoomRequest("Phòng thi đấu thử nghiệm"));
        Assert.True(createRes.Success);
        Assert.NotNull(createRes.RoomId);
        Assert.NotNull(createRes.AdminToken);

        return (manager, createRes.RoomId, createRes.AdminToken);
    }

    [Fact]
    public void UpdateTeam_ValidDetails_Succeeds()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var addRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        Assert.True(addRes.Success);
        var teamId = addRes.Team!.TeamId;

        var updateRes = manager.AdminUpdateTeam(new AdminUpdateTeamRequest(roomId, adminToken, teamId, "Đội Sao Đỏ", "#43A047", 6));
        Assert.True(updateRes.Success);
        Assert.NotNull(updateRes.Team);
        Assert.Equal("Đội Sao Đỏ", updateRes.Team.Name);
        Assert.Equal("#43A047", updateRes.Team.Color);
        Assert.Equal(6, updateRes.Team.Capacity);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        var teamInRoom = room.GetTeam(teamId);
        Assert.NotNull(teamInRoom);
        Assert.Equal("Đội Sao Đỏ", teamInRoom.Name);
        Assert.Equal(6, teamInRoom.Capacity);
    }

    [Fact]
    public void UpdateTeam_ReduceCapacityBelowOccupancy_FailsWithCapacityBelowOccupancy()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var addRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Xanh", "#1E88E5", 4));
        Assert.True(addRes.Success);
        var teamId = addRes.Team!.TeamId;

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);

        // Thêm 2 người chơi vào phòng và gán vào đội
        var p1 = room.TryAddPlayer("Người 1", "conn1", 40);
        var p2 = room.TryAddPlayer("Người 2", "conn2", 40);
        Assert.True(p1.Success && p2.Success);
        p1.Player!.TeamId = teamId;
        p2.Player!.TeamId = teamId;

        // Giảm capacity xuống 1 (trong khi occupancy = 2) -> Bị từ chối
        var failRes = manager.AdminUpdateTeam(new AdminUpdateTeamRequest(roomId, adminToken, teamId, "Đội Xanh", "#1E88E5", 1));
        Assert.False(failRes.Success);
        Assert.Equal(RoomErrorCodes.CapacityBelowOccupancy, failRes.ErrorCode);

        // Giảm capacity về đúng 2 (bằng occupancy) -> Thành công
        var okRes = manager.AdminUpdateTeam(new AdminUpdateTeamRequest(roomId, adminToken, teamId, "Đội Xanh", "#1E88E5", 2));
        Assert.True(okRes.Success);
        Assert.Equal(2, okRes.Team!.Capacity);
    }

    [Fact]
    public void UpdateTeam_DuplicateName_FailsWithDuplicateTeamName()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var team1 = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        var team2 = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Xanh", "#1E88E5", 3));
        Assert.True(team1.Success && team2.Success);

        // Sửa Team 2 thành tên của Team 1
        var res = manager.AdminUpdateTeam(new AdminUpdateTeamRequest(roomId, adminToken, team2.Team!.TeamId, "  đội đỏ  ", "#1E88E5", 3));
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.DuplicateTeamName, res.ErrorCode);
    }

    [Fact]
    public void UpdateTeam_ExceedingTotalCapacity_FailsWithTotalCapacityExceeded()
    {
        var (manager, roomId, adminToken) = CreateTestRoom(maxPlayersPerRoom: 10);

        var team1 = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 1", "#E53935", 5));
        var team2 = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 2", "#1E88E5", 4));
        Assert.True(team1.Success && team2.Success);
        // Tổng hiện tại = 5 + 4 = 9 / 10

        // Cố gắng tăng Team 2 lên 6 -> 5 + 6 = 11 > 10 -> Bị từ chối
        var res = manager.AdminUpdateTeam(new AdminUpdateTeamRequest(roomId, adminToken, team2.Team!.TeamId, "Đội 2", "#1E88E5", 6));
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.TotalCapacityExceeded, res.ErrorCode);

        // Tăng lên 5 -> 5 + 5 = 10 -> Thành công
        var okRes = manager.AdminUpdateTeam(new AdminUpdateTeamRequest(roomId, adminToken, team2.Team!.TeamId, "Đội 2", "#1E88E5", 5));
        Assert.True(okRes.Success);
    }

    [Fact]
    public void UpdateTeam_InvalidTokenOrAlienRoom_FailsAppropriately()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();
        var addRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 1", "#E53935", 3));

        var fakeToken = manager.AdminUpdateTeam(new AdminUpdateTeamRequest(roomId, "fake-token", addRes.Team!.TeamId, "Đội Mới", "#E53935", 3));
        Assert.False(fakeToken.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, fakeToken.ErrorCode);

        var alienRoom = manager.AdminUpdateTeam(new AdminUpdateTeamRequest("alien-room-id", adminToken, addRes.Team!.TeamId, "Đội Mới", "#E53935", 3));
        Assert.False(alienRoom.Success);
        Assert.Equal(RoomErrorCodes.RoomNotFound, alienRoom.ErrorCode);
    }

    [Fact]
    public void RemoveTeam_EmptyTeam_SucceedsAndReindexesDisplayOrder()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var teamA = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội A", "#E53935", 3));
        var teamB = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội B", "#1E88E5", 3));
        var teamC = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội C", "#43A047", 3));

        // Xóa Team B (ở giữa)
        var removeRes = manager.AdminRemoveTeam(new AdminRemoveTeamRequest(roomId, adminToken, teamB.Team!.TeamId));
        Assert.True(removeRes.Success);
        Assert.Equal(teamB.Team.TeamId, removeRes.RemovedTeamId);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        Assert.Equal(2, room.TeamCount);

        var snapshots = room.GetTeamSnapshots();
        Assert.Equal("Đội A", snapshots[0].Name);
        Assert.Equal(0, snapshots[0].DisplayOrder);
        Assert.Equal("Đội C", snapshots[1].Name);
        Assert.Equal(1, snapshots[1].DisplayOrder);
    }

    [Fact]
    public void RemoveTeam_TeamNotEmpty_FailsWithTeamNotEmpty()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var addRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Có Người", "#E53935", 3));
        var teamId = addRes.Team!.TeamId;

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        var p = room.TryAddPlayer("Học Viên 1", "conn1", 40);
        Assert.True(p.Success);
        p.Player!.TeamId = teamId;

        var removeRes = manager.AdminRemoveTeam(new AdminRemoveTeamRequest(roomId, adminToken, teamId));
        Assert.False(removeRes.Success);
        Assert.Equal(RoomErrorCodes.TeamNotEmpty, removeRes.ErrorCode);

        // Đội vẫn còn nguyên trong phòng
        Assert.Equal(1, room.TeamCount);
    }

    [Fact]
    public void ReorderTeams_ValidOrdering_Succeeds()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var teamA = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội A", "#E53935", 3));
        var teamB = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội B", "#1E88E5", 3));
        var teamC = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội C", "#43A047", 3));

        var idA = teamA.Team!.TeamId;
        var idB = teamB.Team!.TeamId;
        var idC = teamC.Team!.TeamId;

        // Đảo thứ tự: B lên đầu, rồi C, rồi A
        var reorderRes = manager.AdminReorderTeams(new AdminReorderTeamsRequest(roomId, adminToken, [idB, idC, idA]));
        Assert.True(reorderRes.Success);
        Assert.NotNull(reorderRes.Teams);

        Assert.Equal(idB, reorderRes.Teams[0].TeamId);
        Assert.Equal(0, reorderRes.Teams[0].DisplayOrder);

        Assert.Equal(idC, reorderRes.Teams[1].TeamId);
        Assert.Equal(1, reorderRes.Teams[1].DisplayOrder);

        Assert.Equal(idA, reorderRes.Teams[2].TeamId);
        Assert.Equal(2, reorderRes.Teams[2].DisplayOrder);
    }

    [Fact]
    public void ReorderTeams_InvalidOrdering_FailsWithInvalidTeamOrder()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var teamA = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội A", "#E53935", 3));
        var teamB = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội B", "#1E88E5", 3));

        var idA = teamA.Team!.TeamId;
        var idB = teamB.Team!.TeamId;

        // Trùng ID [A, A]
        var dupRes = manager.AdminReorderTeams(new AdminReorderTeamsRequest(roomId, adminToken, [idA, idA]));
        Assert.False(dupRes.Success);
        Assert.Equal(RoomErrorCodes.InvalidTeamOrder, dupRes.ErrorCode);

        // Thiếu ID [A]
        var missingRes = manager.AdminReorderTeams(new AdminReorderTeamsRequest(roomId, adminToken, [idA]));
        Assert.False(missingRes.Success);
        Assert.Equal(RoomErrorCodes.InvalidTeamOrder, missingRes.ErrorCode);

        // Chứa ID lạ
        var alienRes = manager.AdminReorderTeams(new AdminReorderTeamsRequest(roomId, adminToken, [idA, "alien-id-123"]));
        Assert.False(alienRes.Success);
        Assert.Equal(RoomErrorCodes.InvalidTeamOrder, alienRes.ErrorCode);
    }
}


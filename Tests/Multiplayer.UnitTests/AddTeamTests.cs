using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public class AddTeamTests
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
    public void AddTeam_ValidDifferentCapacities_Succeeds()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var resRed = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        Assert.True(resRed.Success);
        Assert.NotNull(resRed.Team);
        Assert.Equal("Đội Đỏ", resRed.Team.Name);
        Assert.Equal("#E53935", resRed.Team.Color);
        Assert.Equal(3, resRed.Team.Capacity);
        Assert.Equal(0, resRed.Team.DisplayOrder);

        var resBlue = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Xanh", "#1E88E5", 5));
        Assert.True(resBlue.Success);
        Assert.NotNull(resBlue.Team);
        Assert.Equal("Đội Xanh", resBlue.Team.Name);
        Assert.Equal("#1E88E5", resBlue.Team.Color);
        Assert.Equal(5, resBlue.Team.Capacity);
        Assert.Equal(1, resBlue.Team.DisplayOrder);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        Assert.Equal(2, room.TeamCount);
        var teams = room.GetTeamSnapshots();
        Assert.Equal(2, teams.Count);
        Assert.Equal(8, teams.Sum(t => t.Capacity));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void AddTeam_InvalidName_FailsWithInvalidTeamName(string invalidName)
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var res = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, invalidName, "#E53935", 3));
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.InvalidTeamName, res.ErrorCode);
    }

    [Fact]
    public void AddTeam_DuplicateName_CaseInsensitive_FailsWithDuplicateTeamName()
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var first = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        Assert.True(first.Success);

        var duplicate = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "  đội đỏ  ", "#1E88E5", 4));
        Assert.False(duplicate.Success);
        Assert.Equal(RoomErrorCodes.DuplicateTeamName, duplicate.ErrorCode);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("#GGGGGG")]
    [InlineData("#12")]
    [InlineData("#1234567")]
    [InlineData("")]
    public void AddTeam_InvalidColor_FailsWithInvalidTeamColor(string invalidColor)
    {
        var (manager, roomId, adminToken) = CreateTestRoom();

        var res = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Vàng", invalidColor, 3));
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.InvalidTeamColor, res.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(50)]
    public void AddTeam_InvalidCapacity_FailsWithInvalidTeamCapacity(int invalidCapacity)
    {
        var (manager, roomId, adminToken) = CreateTestRoom(maxPlayersPerTeam: 8);

        var res = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Cam", "#FB8C00", invalidCapacity));
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.InvalidTeamCapacity, res.ErrorCode);
    }

    [Fact]
    public void AddTeam_ExceedingMaxTeamsPerRoom_FailsWithMaxTeamsExceeded()
    {
        var (manager, roomId, adminToken) = CreateTestRoom(maxTeams: 3, maxPlayersPerRoom: 40);

        for (int i = 1; i <= 3; i++)
        {
            var addRes = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, $"Đội {i}", "#E53935", 2));
            Assert.True(addRes.Success);
        }

        var overflow = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 4", "#E53935", 2));
        Assert.False(overflow.Success);
        Assert.Equal(RoomErrorCodes.MaxTeamsExceeded, overflow.ErrorCode);
    }

    [Fact]
    public void AddTeam_ExceedingTotalCapacity_FailsWithTotalCapacityExceeded()
    {
        // Giới hạn phòng tối đa 10 người, mỗi đội tối đa 8 người
        var (manager, roomId, adminToken) = CreateTestRoom(maxPlayersPerRoom: 10, maxPlayersPerTeam: 8);

        var team1 = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 1", "#E53935", 6));
        Assert.True(team1.Success);

        // Đội 2 cần 5 người -> 6 + 5 = 11 > 10 -> Phải bị từ chối
        var team2 = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 2", "#1E88E5", 5));
        Assert.False(team2.Success);
        Assert.Equal(RoomErrorCodes.TotalCapacityExceeded, team2.ErrorCode);

        // Đội 2 với 4 người -> 6 + 4 = 10 -> Phải thành công
        var team2Valid = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội 2", "#1E88E5", 4));
        Assert.True(team2Valid.Success);
    }

    [Fact]
    public void AddTeam_NonAdminOrInvalidToken_FailsWithUnauthorizedAdmin()
    {
        var (manager, roomId, _) = CreateTestRoom();

        var res = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, "fake-admin-token-12345", "Đội Tím", "#8E24AA", 3));
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, res.ErrorCode);
    }

    [Fact]
    public void AddTeam_RoomNotFound_FailsWithRoomNotFound()
    {
        var (manager, _, adminToken) = CreateTestRoom();

        var res = manager.AdminAddTeam(new AdminAddTeamRequest("non-existent-room-id", adminToken, "Đội Tím", "#8E24AA", 3));
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.RoomNotFound, res.ErrorCode);
    }

    [Fact]
    public async Task AddTeam_ConcurrentAddition_TotalCapacityProtectedByAtomicLock()
    {
        // Giới hạn phòng 10 người.
        var (manager, roomId, adminToken) = CreateTestRoom(maxPlayersPerRoom: 10, maxPlayersPerTeam: 8);

        // Thêm trước 1 đội 6 người -> Còn dư đúng 4 chỗ
        var initial = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Nền Tảng", "#E53935", 6));
        Assert.True(initial.Success);

        // 2 luồng đồng thời cố gắng thêm đội có sức chứa 3 người
        // 6 + 3 + 3 = 12 > 10. Chỉ đúng 1 luồng được thành công!
        using var barrier = new Barrier(2);
        AdminAddTeamResponse? resA = null;
        AdminAddTeamResponse? resB = null;

        var taskA = Task.Run(() =>
        {
            barrier.SignalAndWait();
            resA = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Song Hành A", "#1E88E5", 3));
        });

        var taskB = Task.Run(() =>
        {
            barrier.SignalAndWait();
            resB = manager.AdminAddTeam(new AdminAddTeamRequest(roomId, adminToken, "Đội Song Hành B", "#43A047", 3));
        });

        await Task.WhenAll(taskA, taskB);

        Assert.NotNull(resA);
        Assert.NotNull(resB);

        int successCount = (resA.Success ? 1 : 0) + (resB.Success ? 1 : 0);
        Assert.Equal(1, successCount);

        var failed = resA.Success ? resB : resA;
        Assert.False(failed.Success);
        Assert.Equal(RoomErrorCodes.TotalCapacityExceeded, failed.ErrorCode);

        var room = manager.GetRoomById(roomId);
        Assert.NotNull(room);
        Assert.Equal(2, room.TeamCount);
        Assert.Equal(9, room.GetTeamSnapshots().Sum(t => t.Capacity));
    }
}


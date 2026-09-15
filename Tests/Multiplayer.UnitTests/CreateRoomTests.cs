using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

public sealed class CreateRoomTests
{
    private sealed class CollidingCodeGenerator : IRoomCodeGenerator
    {
        private int _callCount = 0;
        public string CollidingCode { get; } = "DC-TEST";
        public string NextUniqueCode { get; } = "DC-UNIQ";

        public string GenerateCode()
        {
            _callCount++;
            return _callCount <= 2 ? CollidingCode : NextUniqueCode;
        }
    }

    [Fact]
    public void CreateRoomSuccessAtLobbyStatus()
    {
        var manager = new RoomManager();
        var request = new CreateRoomRequest("Cuộc thi Minh Đăng", 900, true, false, true, false, false);
        var response = manager.CreateRoom(request, "conn_123");

        Assert.True(response.Success);
        Assert.NotNull(response.RoomId);
        Assert.NotNull(response.RoomCode);
        Assert.StartsWith("DC-", response.RoomCode);
        Assert.NotNull(response.AdminToken);
        Assert.NotNull(response.Snapshot);

        Assert.Equal(RoomStatus.Lobby, response.Snapshot.Status);
        Assert.Equal("Cuộc thi Minh Đăng", response.Snapshot.RoomName);
        Assert.Equal(900, response.Snapshot.TimeLimitSeconds);
        Assert.Equal(1, response.Snapshot.Version);

        // Verify stored room instance
        var room = manager.GetRoomById(response.RoomId);
        Assert.NotNull(room);
        Assert.Equal(response.RoomCode, room.RoomCode);
        Assert.True(room.VerifyAdmin(response.AdminToken));
        Assert.False(room.VerifyAdmin("wrong_token"));
    }

    [Fact]
    public void PublicSnapshotDoesNotExposeAdminSecret()
    {
        var manager = new RoomManager();
        var request = new CreateRoomRequest("Phòng Bảo Mật", 900);
        var response = manager.CreateRoom(request);

        Assert.True(response.Success);
        var snapshot = response.Snapshot;
        Assert.NotNull(snapshot);

        // Verify snapshot does not have any property named or containing admin secret
        var snapshotProps = typeof(RoomSnapshot).GetProperties();
        Assert.DoesNotContain(snapshotProps, p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                                                 p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("", RoomErrorCodes.InvalidRoomName)]
    [InlineData("  ", RoomErrorCodes.InvalidRoomName)]
    [InlineData("ab", RoomErrorCodes.InvalidRoomName)] // < 3 chars
    [InlineData("Phòng này có tên quá dài vượt quá giới hạn năm mươi ký tự cho phép của hệ thống", RoomErrorCodes.InvalidRoomName)]
    [InlineData("Hợp lệ", RoomErrorCodes.InvalidTimeLimit, 10)] // time limit too short (< 60s)
    [InlineData("Hợp lệ", RoomErrorCodes.InvalidTimeLimit, 100000)] // time limit too long (> 7200s)
    public void InvalidInputRejectsRoomCreation(string name, string expectedError, int timeLimit = 900)
    {
        var manager = new RoomManager();
        var request = new CreateRoomRequest(name, timeLimit);
        var response = manager.CreateRoom(request);

        Assert.False(response.Success);
        Assert.Equal(expectedError, response.ErrorCode);
        Assert.Null(response.RoomId);
        Assert.Null(response.RoomCode);
        Assert.Null(response.AdminToken);
    }

    [Fact]
    public void CodeCollisionTriggersRetryAndSucceeds()
    {
        var codeGen = new CollidingCodeGenerator();
        var manager = new RoomManager(codeGenerator: codeGen);

        // First room takes DC-TEST
        var res1 = manager.CreateRoom(new CreateRoomRequest("Phòng 1", 900));
        Assert.True(res1.Success);
        Assert.Equal("DC-TEST", res1.RoomCode);

        // Second room collides once on DC-TEST, retries, and gets DC-UNIQ
        var res2 = manager.CreateRoom(new CreateRoomRequest("Phòng 2", 900));
        Assert.True(res2.Success);
        Assert.Equal("DC-UNIQ", res2.RoomCode);
        Assert.NotEqual(res1.RoomId, res2.RoomId);
    }

    [Fact]
    public void TwoCreatedRoomsHaveDifferentCodesAndTokens()
    {
        var manager = new RoomManager();
        var res1 = manager.CreateRoom(new CreateRoomRequest("Phòng Alpha", 600));
        var res2 = manager.CreateRoom(new CreateRoomRequest("Phòng Beta", 1200));

        Assert.True(res1.Success && res2.Success);
        Assert.NotEqual(res1.RoomId, res2.RoomId);
        Assert.NotEqual(res1.RoomCode, res2.RoomCode);
        Assert.NotEqual(res1.AdminToken, res2.AdminToken);
    }
}


using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

public sealed class JoinRoomTests
{
    [Fact]
    public void JoinRoomValidRequestSucceeds()
    {
        var manager = new RoomManager();
        var createRes = manager.CreateRoom(new CreateRoomRequest("Phòng Thi Đấu", 900));
        Assert.True(createRes.Success);

        var joinRes = manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, "Minh Anh"), "conn_1");

        Assert.True(joinRes.Success);
        Assert.NotNull(joinRes.PlayerId);
        Assert.NotNull(joinRes.ReconnectToken);
        Assert.NotNull(joinRes.Room);
        Assert.NotNull(joinRes.Players);
        Assert.Single(joinRes.Players);

        var player = joinRes.Players[0];
        Assert.Equal(joinRes.PlayerId, player.PlayerId);
        Assert.Equal("Minh Anh", player.DisplayName);
        Assert.False(player.IsReady);
        Assert.True(player.IsConnected);
    }

    [Fact]
    public async Task DuplicateDisplayNameSimultaneousRequestsOnlyOneSucceeds()
    {
        var manager = new RoomManager();
        var createRes = manager.CreateRoom(new CreateRoomRequest("Phòng Tranh Tên", 900));
        Assert.True(createRes.Success);

        // Chuẩn bị barrier để 2 luồng join cùng lúc
        var barrier = new Barrier(2);

        var task1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, "Thành Nam"), "conn_a");
        });

        var task2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            // Cùng tên nhưng viết hoa thường khác nhau
            return manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, "  thành nam  "), "conn_b");
        });

        var results = await Task.WhenAll(task1, task2);
        var successes = results.Count(r => r.Success);
        var failures = results.Count(r => !r.Success && r.ErrorCode == RoomErrorCodes.DuplicateDisplayName);

        Assert.Equal(1, successes);
        Assert.Equal(1, failures);
    }

    [Fact]
    public void NonExistentRoomCodeIsRejected()
    {
        var manager = new RoomManager();
        var res = manager.JoinRoom(new JoinRoomRequest("DC-NONE", "Người Chơi"), "conn_x");

        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.RoomNotFound, res.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("Tên người chơi này quá dài vượt quá hai mươi lăm ký tự")]
    public void InvalidDisplayNameIsRejected(string name)
    {
        var manager = new RoomManager();
        var createRes = manager.CreateRoom(new CreateRoomRequest("Phòng Kiểm Tra", 900));

        var res = manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, name), "conn_x");
        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.InvalidDisplayName, res.ErrorCode);
    }

    [Fact]
    public void RoomFullRejectsAdditionalPlayers()
    {
        var options = Options.Create(new RoomServerOptions { MaxPlayersPerRoom = 2 });
        var manager = new RoomManager(options);
        var createRes = manager.CreateRoom(new CreateRoomRequest("Phòng Nhỏ", 900));

        var p1 = manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, "Người 1"), "conn_1");
        var p2 = manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, "Người 2"), "conn_2");
        var p3 = manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, "Người 3"), "conn_3");

        Assert.True(p1.Success);
        Assert.True(p2.Success);
        Assert.False(p3.Success);
        Assert.Equal(RoomErrorCodes.RoomFull, p3.ErrorCode);
    }

    [Fact]
    public void PlayerSnapshotDoesNotExposeSecretsOrOtherTokens()
    {
        var manager = new RoomManager();
        var createRes = manager.CreateRoom(new CreateRoomRequest("Phòng Riêng Tư", 900));

        var p1 = manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, "Người A"), "conn_1");
        var p2 = manager.JoinRoom(new JoinRoomRequest(createRes.RoomCode!, "Người B"), "conn_2");

        Assert.True(p1.Success && p2.Success);
        Assert.NotEqual(p1.ReconnectToken, p2.ReconnectToken);

        // PlayerSnapshot public model does not contain reconnect token property
        var props = typeof(PlayerSnapshot).GetProperties();
        Assert.DoesNotContain(props, p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }
}


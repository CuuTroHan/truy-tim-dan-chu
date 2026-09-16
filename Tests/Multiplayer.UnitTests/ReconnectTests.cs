using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class ReconnectTests
{
    [Fact]
    public void ResumeBeforeTtl_RebindsAndRotatesToken()
    {
        var manager = NewManager();
        var created = manager.CreateRoom(new CreateRoomRequest("Reconnect", 60));
        Assert.True(created.Success, created.ErrorCode);
        var joined = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "old-connection");
        Assert.True(joined.Success, joined.ErrorCode);
        var room = manager.GetRoomById(created.RoomId!)!;
        room.UtcNowProvider = () => DateTimeOffset.UtcNow.AddSeconds(1);
        room.TryMarkPlayerDisconnected("old-connection", out _);

        var resumed = manager.ResumePlayer(new ResumePlayerRequest(created.RoomCode!, joined.PlayerId!, joined.ReconnectToken!), "new-connection");

        Assert.True(resumed.Response.Success);
        Assert.NotEqual(joined.ReconnectToken, resumed.Response.ReconnectToken);
        Assert.Equal("new-connection", room.GetPlayer(joined.PlayerId!)!.ConnectionId);
        Assert.True(room.IsPlayerConnectionBound(joined.PlayerId!, "new-connection"));
    }

    [Fact]
    public void OldTokenAfterResume_IsRejected()
    {
        var manager = NewManager();
        var created = manager.CreateRoom(new CreateRoomRequest("Reconnect", 60));
        Assert.True(created.Success, created.ErrorCode);
        var joined = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "old-connection");
        Assert.True(joined.Success, joined.ErrorCode);
        var room = manager.GetRoomById(created.RoomId!)!;
        room.TryMarkPlayerDisconnected("old-connection", out _);
        var resumed = manager.ResumePlayer(new ResumePlayerRequest(created.RoomCode!, joined.PlayerId!, joined.ReconnectToken!), "new-connection");

        room.TryMarkPlayerDisconnected("new-connection", out _);
        var replay = manager.ResumePlayer(new ResumePlayerRequest(created.RoomCode!, joined.PlayerId!, joined.ReconnectToken!), "third-connection");

        Assert.False(replay.Response.Success);
        Assert.Equal(RoomErrorCodes.InvalidReconnectToken, replay.Response.ErrorCode);
        Assert.Equal("new-connection", room.GetPlayer(joined.PlayerId!)!.ConnectionId);
    }

    [Fact]
    public void ResumeAfterTtl_IsRejected()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var manager = NewManager();
        var created = manager.CreateRoom(new CreateRoomRequest("Reconnect", 60));
        Assert.True(created.Success, created.ErrorCode);
        var joined = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "old-connection");
        Assert.True(joined.Success, joined.ErrorCode);
        var room = manager.GetRoomById(created.RoomId!)!;
        room.UtcNowProvider = () => now;
        room.TryMarkPlayerDisconnected("old-connection", out _);
        room.UtcNowProvider = () => now.AddSeconds(91);

        var resumed = manager.ResumePlayer(new ResumePlayerRequest(created.RoomCode!, joined.PlayerId!, joined.ReconnectToken!), "new-connection");

        Assert.False(resumed.Response.Success);
        Assert.Equal(RoomErrorCodes.ReconnectExpired, resumed.Response.ErrorCode);
    }

    [Fact]
    public void HeartbeatRequiresCurrentConnectionBinding()
    {
        var manager = NewManager();
        var created = manager.CreateRoom(new CreateRoomRequest("Reconnect", 60));
        Assert.True(created.Success, created.ErrorCode);
        var joined = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "connection");
        Assert.True(joined.Success, joined.ErrorCode);

        var rejected = manager.Heartbeat(new HeartbeatRequest(created.RoomId!, joined.PlayerId!), "other-connection");
        var accepted = manager.Heartbeat(new HeartbeatRequest(created.RoomId!, joined.PlayerId!), "connection");

        Assert.False(rejected.Success);
        Assert.Equal(RoomErrorCodes.ConnectionNotBound, rejected.ErrorCode);
        Assert.True(accepted.Success);
    }

    private static RoomManager NewManager() => new(Options.Create(new RoomServerOptions
    {
        MinTimeLimitSeconds = 60,
        MaxTimeLimitSeconds = 600,
        MaxPlayersPerRoom = 10
    }));
}

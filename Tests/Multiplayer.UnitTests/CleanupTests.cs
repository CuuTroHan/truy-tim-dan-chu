using Microsoft.Extensions.Options;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class CleanupTests
{
    [Fact]
    public void DisconnectBeforeTtl_KeepsSessionAndOccupancy()
    {
        var manager = NewManager();
        var created = manager.CreateRoom(new CreateRoomRequest("Cleanup", 60));
        var joined = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "conn");
        var room = manager.GetRoomById(created.RoomId!)!;
        room.UtcNowProvider = () => DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        room.TryMarkPlayerDisconnected("conn", out _);

        var result = room.CleanupExpiredSessions(DateTimeOffset.Parse("2026-01-01T00:00:30Z"), TimeSpan.FromMinutes(1));

        Assert.Empty(result.ExpiredPlayerIds);
        Assert.Equal(1, room.PlayerCount);
        Assert.NotNull(room.GetPlayer(joined.PlayerId!));
    }

    [Fact]
    public void AtTtl_ExpiresExactlyOnce_AndReturnsSlot()
    {
        var manager = NewManager();
        var created = manager.CreateRoom(new CreateRoomRequest("Cleanup", 60));
        manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "conn");
        var room = manager.GetRoomById(created.RoomId!)!;
        var disconnectedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        room.UtcNowProvider = () => disconnectedAt;
        room.TryMarkPlayerDisconnected("conn", out var player);

        var first = room.CleanupExpiredSessions(disconnectedAt.AddMinutes(1), TimeSpan.FromMinutes(1));
        var second = room.CleanupExpiredSessions(disconnectedAt.AddMinutes(2), TimeSpan.FromMinutes(1));

        Assert.Single(first.ExpiredPlayerIds);
        Assert.Empty(second.ExpiredPlayerIds);
        Assert.Equal(0, room.PlayerCount);
        Assert.Null(room.GetPlayer(player!.PlayerId));
    }

    [Fact]
    public void ResumeBeforeCleanup_WinsTheRoomLock()
    {
        var manager = NewManager();
        var created = manager.CreateRoom(new CreateRoomRequest("Cleanup", 60));
        var joined = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "old");
        var room = manager.GetRoomById(created.RoomId!)!;
        var disconnectedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        room.UtcNowProvider = () => disconnectedAt;
        room.TryMarkPlayerDisconnected("old", out _);
        room.UtcNowProvider = () => disconnectedAt.AddSeconds(30);

        var resumed = manager.ResumePlayer(new ResumePlayerRequest(created.RoomCode!, joined.PlayerId!, joined.ReconnectToken!), "new");
        var cleanup = room.CleanupExpiredSessions(disconnectedAt.AddMinutes(2), TimeSpan.FromMinutes(1));

        Assert.True(resumed.Response.Success);
        Assert.Empty(cleanup.ExpiredPlayerIds);
        Assert.Equal(1, room.PlayerCount);
        Assert.Equal("new", room.GetPlayer(joined.PlayerId!)!.ConnectionId);
    }

    [Fact]
    public void LobbyWithoutActors_ClosesThenIsRemovableByRetentionPolicy()
    {
        var manager = NewManager();
        var created = manager.CreateRoom(new CreateRoomRequest("Cleanup", 60));
        var room = manager.GetRoomById(created.RoomId!)!;
        var closedAt = room.LastActivityAt.AddMinutes(61);

        Assert.Equal(RoomCleanupDecision.Closed,
            room.EvaluateRoomCleanup(closedAt, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(10)));
        Assert.Equal(RoomStatus.Closed, room.Status);
        Assert.Equal(RoomCleanupDecision.Remove,
            room.EvaluateRoomCleanup(closedAt.AddMinutes(10), TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(10)));
        Assert.True(manager.RemoveRoom(room));
        Assert.Null(manager.GetRoomByCode(created.RoomCode));
    }

    private static RoomManager NewManager() => new(Options.Create(new RoomServerOptions
    {
        MinTimeLimitSeconds = 60,
        MaxTimeLimitSeconds = 600,
        MaxPlayersPerRoom = 10
    }));
}

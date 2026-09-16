using Microsoft.Extensions.Options;
using Xunit;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;

public sealed class EndRoomTests
{
    private static (RoomManager Manager, CreateRoomResponse Room, string Token, string MatchId) Playing(bool allowLateJoin = false, bool adminCanPlay = false)
    {
        var manager = new RoomManager(Options.Create(new RoomServerOptions { ReconnectGraceSeconds = 30 }));
        var created = manager.CreateRoom(new CreateRoomRequest("Test room", 900, true, allowLateJoin, true, adminCanPlay), "admin");
        var token = created.AdminToken!;
        var roomId = created.RoomId!;
        manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Red", "#e33", 4));
        manager.AdminAddTeam(new AdminAddTeamRequest(roomId, token, "Blue", "#38f", 4));
        var a = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "a");
        var b = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Bob"), "b");
        manager.JoinTeam(new JoinTeamRequest(roomId, a.PlayerId!, manager.GetRoomById(roomId)!.GetTeamSnapshots()[0].TeamId));
        manager.JoinTeam(new JoinTeamRequest(roomId, b.PlayerId!, manager.GetRoomById(roomId)!.GetTeamSnapshots()[1].TeamId));
        manager.SetReady(new SetReadyRequest(roomId, a.PlayerId!, true));
        manager.SetReady(new SetReadyRequest(roomId, b.PlayerId!, true));
        var start = manager.AdminStartMatch(new AdminStartMatchRequest(roomId, token, 1));
        var id = start.MatchId!;
        manager.GetRoomById(roomId)!.TryForceAdvanceToPlaying(id);
        return (manager, created, token, id);
    }

    [Fact]
    public void AdminEndProducesEarlyResultAndFinishedRoom()
    {
        var x = Playing();
        var result = x.Manager.AdminEndMatch(new AdminEndMatchRequest(x.Room.RoomId!, x.Token, x.MatchId, "end-1"), "admin");
        Assert.True(result.Success);
        Assert.Equal(RoomStatus.Finished, x.Manager.GetRoomById(x.Room.RoomId!)!.Status);
        Assert.NotNull(result.Event);
        Assert.All(result.Event!.Results!.Teams, t => Assert.NotEqual(TeamResultStatus.TimedOut, t.Status));
    }

    [Fact]
    public void CancelAndCloseAreIdempotentAndClosedIsTerminal()
    {
        var x = Playing();
        var cancelled = x.Manager.AdminCancelMatch(new AdminCancelMatchRequest(x.Room.RoomId!, x.Token, x.MatchId, "cancel-1"), "admin");
        Assert.True(cancelled.Success);
        Assert.Equal(MatchEndReason.Cancelled, x.Manager.GetRoomById(x.Room.RoomId!)!.MatchEndReason);
        var closed = x.Manager.AdminCloseRoom(new AdminCloseRoomRequest(x.Room.RoomId!, x.Token, "close-1"), "admin");
        Assert.True(closed.Success);
        Assert.Equal(RoomStatus.Closed, x.Manager.GetRoomById(x.Room.RoomId!)!.Status);
        var again = x.Manager.AdminCloseRoom(new AdminCloseRoomRequest(x.Room.RoomId!, x.Token, "close-2"), "admin");
        Assert.False(again.Success);
        Assert.Equal(RoomErrorCodes.RoomAlreadyClosed, again.ErrorCode);
    }
}

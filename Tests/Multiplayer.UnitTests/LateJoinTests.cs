using Microsoft.Extensions.Options;
using Xunit;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;

public sealed class LateJoinTests
{
    [Fact]
    public void LateJoinRequiresFlagAndPreservesTeamGame()
    {
        var manager = new RoomManager(Options.Create(new RoomServerOptions()));
        var created = manager.CreateRoom(new CreateRoomRequest("Late join", 900, true, true), "admin");
        var token = created.AdminToken!;
        manager.AdminAddTeam(new AdminAddTeamRequest(created.RoomId!, token, "Red", "#e33", 2));
        manager.AdminAddTeam(new AdminAddTeamRequest(created.RoomId!, token, "Blue", "#38f", 2));
        var a = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Alice"), "a");
        var teams = manager.GetRoomById(created.RoomId!)!.GetTeamSnapshots();
        manager.JoinTeam(new JoinTeamRequest(created.RoomId!, a.PlayerId!, teams[0].TeamId));
        manager.SetReady(new SetReadyRequest(created.RoomId!, a.PlayerId!, true));
        var b = manager.JoinRoom(new JoinRoomRequest(created.RoomCode!, "Bob"), "b");
        manager.JoinTeam(new JoinTeamRequest(created.RoomId!, b.PlayerId!, teams[1].TeamId));
        manager.SetReady(new SetReadyRequest(created.RoomId!, b.PlayerId!, true));
        var start = manager.AdminStartMatch(new AdminStartMatchRequest(created.RoomId!, token, 1));
        manager.GetRoomById(created.RoomId!)!.TryForceAdvanceToPlaying(start.MatchId!);
        var late = manager.LateJoin(new LateJoinRequest(created.RoomCode!, "Late", teams[0].TeamId, "quang", "late-1"), "late-conn");
        Assert.True(late.Success);
        Assert.Equal(start.MatchId, late.TeamState!.MatchId);
        Assert.Equal(2, manager.GetRoomById(created.RoomId!)!.GetTeamGame(teams[0].TeamId)!.MemberCount);
    }
}

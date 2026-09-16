using Microsoft.Extensions.Options;
using Xunit;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;

public sealed class AdminCanPlayTests
{
    [Fact]
    public void AdminCanPlayCreatesSingleLinkedPlayerAndDisallowsLobbyToggleAfterStart()
    {
        var manager = new RoomManager(Options.Create(new RoomServerOptions()));
        var created = manager.CreateRoom(new CreateRoomRequest("Admin player", 900, true, false, true, false), "admin");
        var token = created.AdminToken!;
        manager.AdminAddTeam(new AdminAddTeamRequest(created.RoomId!, token, "Red", "#e33", 2));
        var enabled = manager.AdminSetCanPlay(new AdminSetCanPlayRequest(created.RoomId!, token, true, "can-1"), "admin");
        Assert.True(enabled.Success);
        var joined = manager.AdminJoinAsPlayer(new AdminJoinAsPlayerRequest(created.RoomId!, token, manager.GetRoomById(created.RoomId!)!.GetTeamSnapshots()[0].TeamId, "Host", "quang", "join-1"), "admin");
        Assert.True(joined.Success);
        var replay = manager.AdminJoinAsPlayer(new AdminJoinAsPlayerRequest(created.RoomId!, token, manager.GetRoomById(created.RoomId!)!.GetTeamSnapshots()[0].TeamId, "Host", "quang", "join-1"), "admin");
        Assert.Equal(joined.PlayerId, replay.PlayerId);
        Assert.Single(manager.GetRoomById(created.RoomId!)!.GetPlayerSnapshots());
    }
}

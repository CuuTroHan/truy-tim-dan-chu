using Microsoft.Extensions.Options;
using Xunit;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;

public sealed class AdminReconnectTests
{
    [Fact]
    public void ResumeAdminRotatesTokenAndSupersedesConnection()
    {
        var manager = new RoomManager(Options.Create(new RoomServerOptions()));
        var created = manager.CreateRoom(new CreateRoomRequest("Admin reconnect"), "old-admin");
        var response = manager.ResumeAdmin(new ResumeAdminRequest(created.RoomId!, created.AdminToken!), "new-admin");
        Assert.True(response.Success);
        Assert.NotEqual(created.AdminToken, response.AdminToken);
        Assert.Equal("new-admin", manager.GetRoomById(created.RoomId!)!.AdminConnectionId);
        Assert.False(manager.IsAdminConnectionBound(created.RoomId!, created.AdminToken!, "old-admin"));
        Assert.True(manager.IsAdminConnectionBound(created.RoomId!, response.AdminToken!, "new-admin"));
    }
}

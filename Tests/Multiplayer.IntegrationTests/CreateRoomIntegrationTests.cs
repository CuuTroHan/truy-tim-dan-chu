using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

public sealed class CreateRoomIntegrationTests
{
    private static async Task<WebApplication> StartServer()
    {
        var app = ServerBootstrap.Build([
            "--urls", "http://127.0.0.1:0",
            "--Multiplayer:AllowedOrigins:0", "http://localhost:5267",
            "--Logging:LogLevel:Default", "Warning"
        ]);
        await app.StartAsync();
        return app;
    }

    private static Uri HubUri(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new Uri(address + ConnectionProtocol.HubPath);
    }

    [Fact]
    public async Task RealSignalRClientCanCreateRoomAndReceiveAdminCredentials()
    {
        await using var app = await StartServer();
        await using var hub = new HubConnectionBuilder().WithUrl(HubUri(app)).Build();
        await hub.StartAsync();

        // 1. Handshake first
        var handshake = await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        Assert.True(handshake.Accepted);

        // 2. Create room
        var request = new CreateRoomRequest("Minh Đăng Hợp Tác", 900, true, false, true, false, false);
        var response = await hub.InvokeAsync<CreateRoomResponse>("CreateRoom", request);

        Assert.True(response.Success);
        Assert.NotNull(response.RoomId);
        Assert.NotNull(response.RoomCode);
        Assert.StartsWith("DC-", response.RoomCode);
        Assert.NotNull(response.AdminToken);
        Assert.NotNull(response.Snapshot);
        Assert.Equal("Minh Đăng Hợp Tác", response.Snapshot.RoomName);
        Assert.Equal(RoomStatus.Lobby, response.Snapshot.Status);

        // 3. Invalid room request returns error
        var invalidReq = new CreateRoomRequest("", 900);
        var invalidRes = await hub.InvokeAsync<CreateRoomResponse>("CreateRoom", invalidReq);
        Assert.False(invalidRes.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomName, invalidRes.ErrorCode);

        // 4. Create second room returns different code
        var response2 = await hub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Hai", 600));
        Assert.True(response2.Success);
        Assert.NotEqual(response.RoomCode, response2.RoomCode);
        Assert.NotEqual(response.AdminToken, response2.AdminToken);
    }
}


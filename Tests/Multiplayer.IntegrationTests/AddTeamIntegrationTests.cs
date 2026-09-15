using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class AddTeamIntegrationTests
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
    public async Task SignalR_AddTeam_BroadcastsToAllClientsInRoom_AndRejectsNonAdmin()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var adminTeamEvents = new List<TeamSnapshot>();
        adminHub.On<TeamSnapshot>("TeamAdded", t => adminTeamEvents.Add(t));

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Đấu Đội", 900));
        Assert.True(createRes.Success);
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;
        var adminToken = createRes.AdminToken!;

        // 2. Player A joins
        await using var playerAHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerAHub.StartAsync();
        await playerAHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var playerATeamEvents = new List<TeamSnapshot>();
        playerAHub.On<TeamSnapshot>("TeamAdded", t => playerATeamEvents.Add(t));

        var joinA = await playerAHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Người Chơi A"));
        Assert.True(joinA.Success);

        // 3. Player B joins
        await using var playerBHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerBHub.StartAsync();
        await playerBHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var playerBTeamEvents = new List<TeamSnapshot>();
        playerBHub.On<TeamSnapshot>("TeamAdded", t => playerBTeamEvents.Add(t));

        var joinB = await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Người Chơi B"));
        Assert.True(joinB.Success);

        // 4. Room 2 Client C (for isolation testing)
        await using var clientCHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await clientCHub.StartAsync();
        await clientCHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var clientCTeamEvents = new List<TeamSnapshot>();
        clientCHub.On<TeamSnapshot>("TeamAdded", t => clientCTeamEvents.Add(t));

        var room2Res = await clientCHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Cách Ly", 900));
        Assert.True(room2Res.Success);

        // 5. Admin adds "Đội Đỏ" (capacity 3)
        var addRedRes = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        Assert.True(addRedRes.Success);
        Assert.NotNull(addRedRes.Team);
        Assert.Equal("Đội Đỏ", addRedRes.Team.Name);
        Assert.Equal(3, addRedRes.Team.Capacity);

        // Wait briefly for network propagation
        await Task.Delay(100);

        // Verify Admin, Player A, Player B received "Đội Đỏ"
        Assert.Single(adminTeamEvents);
        Assert.Equal("Đội Đỏ", adminTeamEvents[0].Name);

        Assert.Single(playerATeamEvents);
        Assert.Equal("Đội Đỏ", playerATeamEvents[0].Name);
        Assert.Equal(3, playerATeamEvents[0].Capacity);
        Assert.Equal("#E53935", playerATeamEvents[0].Color);

        Assert.Single(playerBTeamEvents);
        Assert.Equal("Đội Đỏ", playerBTeamEvents[0].Name);

        // Verify Client C in other room received nothing
        Assert.Empty(clientCTeamEvents);

        // 6. Admin adds "Đội Xanh" (capacity 5)
        var addBlueRes = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đội Xanh", "#1E88E5", 5));
        Assert.True(addBlueRes.Success);

        await Task.Delay(100);

        Assert.Equal(2, adminTeamEvents.Count);
        Assert.Equal(2, playerATeamEvents.Count);
        Assert.Equal(2, playerBTeamEvents.Count);
        Assert.Equal("Đội Xanh", playerATeamEvents[1].Name);
        Assert.Equal(5, playerATeamEvents[1].Capacity);
        Assert.Empty(clientCTeamEvents);

        // 7. Player A attempts to call AdminAddTeam with forged admin token
        var forbiddenRes = await playerAHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, "forged-fake-token", "Đội Hack", "#000000", 2));
        Assert.False(forbiddenRes.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, forbiddenRes.ErrorCode);

        // Verify no extra TeamAdded event was emitted
        await Task.Delay(100);
        Assert.Equal(2, playerATeamEvents.Count);
        Assert.Equal(2, playerBTeamEvents.Count);
    }
}


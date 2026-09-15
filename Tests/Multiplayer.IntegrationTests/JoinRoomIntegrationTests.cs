using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

public sealed class JoinRoomIntegrationTests
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
    public async Task RealSignalRMultiClientJoinAndEventBroadcast()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var adminJoinedEvents = new List<PlayerSnapshot>();
        adminHub.On<PlayerSnapshot>("PlayerJoined", p => adminJoinedEvents.Add(p));

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Tích Hợp", 900));
        Assert.True(createRes.Success);
        var roomCode = createRes.RoomCode!;

        // 2. Player A connects and joins
        await using var playerAHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerAHub.StartAsync();
        await playerAHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var playerAJoinedEvents = new List<PlayerSnapshot>();
        playerAHub.On<PlayerSnapshot>("PlayerJoined", p => playerAJoinedEvents.Add(p));

        var joinA = await playerAHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Học Viên A"));
        Assert.True(joinA.Success);
        Assert.Equal("Học Viên A", joinA.Players?.Single().DisplayName);

        // Verify Admin received PlayerJoined for Player A
        await Task.Delay(100);
        Assert.Single(adminJoinedEvents);
        Assert.Equal("Học Viên A", adminJoinedEvents[0].DisplayName);

        // 3. Player B tries to join with duplicate name (case insensitive)
        await using var playerBHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerBHub.StartAsync();
        await playerBHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var duplicateRes = await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "  học viên a  "));
        Assert.False(duplicateRes.Success);
        Assert.Equal(RoomErrorCodes.DuplicateDisplayName, duplicateRes.ErrorCode);

        // 4. Player B joins with unique name
        var joinB = await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Học Viên B"));
        Assert.True(joinB.Success);

        await Task.Delay(100);
        Assert.Equal(2, adminJoinedEvents.Count);
        Assert.Equal("Học Viên B", adminJoinedEvents[1].DisplayName);
        Assert.Single(playerAJoinedEvents);
        Assert.Equal("Học Viên B", playerAJoinedEvents[0].DisplayName);

        // 5. Room Isolation: Client C in a DIFFERENT room must NOT receive any events from this room
        await using var clientCHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await clientCHub.StartAsync();
        await clientCHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var cEvents = new List<PlayerSnapshot>();
        clientCHub.On<PlayerSnapshot>("PlayerJoined", p => cEvents.Add(p));

        var room2Res = await clientCHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Khác", 900));
        Assert.True(room2Res.Success);

        // Assert client C has 0 events from room 1
        Assert.Empty(cEvents);
    }
}


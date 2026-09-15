using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class RosterAdminIntegrationTests
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
    public async Task SignalR_AdminMoveAndKickPlayer_BroadcastsAndEvictsProperly()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Điều Hành", 900, AllowSelfTeamSelection: false));
        Assert.True(createRes.Success);
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;
        var adminToken = createRes.AdminToken!;

        // 2. Add Team Red (Cap 1) and Blue (Cap 2)
        var redRes = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đỏ", "#E53935", 1));
        var blueRes = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Xanh", "#1E88E5", 2));
        var redId = redRes.Team!.TeamId;
        var blueId = blueRes.Team!.TeamId;

        // 3. Player A and Player B join room
        await using var playerAHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerAHub.StartAsync();
        await playerAHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var aJoin = await playerAHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player A"));
        var aId = aJoin.PlayerId!;

        await using var playerBHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerBHub.StartAsync();
        await playerBHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var bJoin = await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player B"));
        var bId = bJoin.PlayerId!;

        // Track events on Player A and B
        PlayerKickedEvent? aKickedEvent = null;
        playerAHub.On<PlayerKickedEvent>("KickedFromRoom", ev => aKickedEvent = ev);

        string? bObservedPlayerLeft = null;
        playerBHub.On<string>("PlayerLeft", id => bObservedPlayerLeft = id);

        PlayerTeamChangedEvent? bLastTeamChanged = null;
        playerBHub.On<PlayerTeamChangedEvent>("PlayerTeamChanged", ev => bLastTeamChanged = ev);

        // 4. Admin moves Player A to Red (Cap 1)
        var moveARes = await adminHub.InvokeAsync<AdminMovePlayerResponse>("AdminMovePlayer",
            new AdminMovePlayerRequest(roomId, adminToken, aId, redId));
        Assert.True(moveARes.Success);
        Assert.Equal(redId, moveARes.NewTeamId);

        await Task.Delay(100);
        Assert.NotNull(bLastTeamChanged);
        Assert.Equal(aId, bLastTeamChanged.PlayerId);
        Assert.Equal(redId, bLastTeamChanged.NewTeamId);

        // 5. Admin tries to move Player B to Red (full) -> FAILS
        var moveBFullRes = await adminHub.InvokeAsync<AdminMovePlayerResponse>("AdminMovePlayer",
            new AdminMovePlayerRequest(roomId, adminToken, bId, redId));
        Assert.False(moveBFullRes.Success);
        Assert.Equal(RoomErrorCodes.TeamFull, moveBFullRes.ErrorCode);

        // 6. Admin moves Player B to Blue -> SUCCEEDS
        var moveBRes = await adminHub.InvokeAsync<AdminMovePlayerResponse>("AdminMovePlayer",
            new AdminMovePlayerRequest(roomId, adminToken, bId, blueId));
        Assert.True(moveBRes.Success);

        // 7. Non-admin cannot kick -> FAILS
        var fakeKick = await playerBHub.InvokeAsync<AdminKickPlayerResponse>("AdminKickPlayer",
            new AdminKickPlayerRequest(roomId, "wrong-token", aId));
        Assert.False(fakeKick.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, fakeKick.ErrorCode);

        // 8. Admin kicks Player A
        var kickRes = await adminHub.InvokeAsync<AdminKickPlayerResponse>("AdminKickPlayer",
            new AdminKickPlayerRequest(roomId, adminToken, aId, "Không hợp lệ"));
        Assert.True(kickRes.Success);
        Assert.Equal(aId, kickRes.KickedPlayerId);

        await Task.Delay(150);
        // Player A received kick event
        Assert.NotNull(aKickedEvent);
        Assert.Equal(aId, aKickedEvent.PlayerId);
        Assert.Equal("Không hợp lệ", aKickedEvent.Reason);

        // Player B observed Player A left
        Assert.Equal(aId, bObservedPlayerLeft);

        // 9. Now Red is free (0/1) -> Admin can move Player B to Red
        var moveBSwitch = await adminHub.InvokeAsync<AdminMovePlayerResponse>("AdminMovePlayer",
            new AdminMovePlayerRequest(roomId, adminToken, bId, redId));
        Assert.True(moveBSwitch.Success);
        Assert.Equal(redId, moveBSwitch.NewTeamId);
        Assert.Equal(blueId, moveBSwitch.OldTeamId);
    }
}


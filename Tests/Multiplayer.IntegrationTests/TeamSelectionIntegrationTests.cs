using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class TeamSelectionIntegrationTests
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
    public async Task SignalR_JoinAndLeaveTeam_BroadcastsAndEnforcesCapacity()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Chọn Đội", 900));
        Assert.True(createRes.Success);
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;
        var adminToken = createRes.AdminToken!;

        // 2. Add Team Red (Cap 1) and Team Blue (Cap 2)
        var redRes = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đỏ", "#E53935", 1));
        var blueRes = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Xanh", "#1E88E5", 2));
        Assert.True(redRes.Success && blueRes.Success);
        var redId = redRes.Team!.TeamId;
        var blueId = blueRes.Team!.TeamId;

        // 3. Player A and Player B join room
        await using var playerAHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerAHub.StartAsync();
        await playerAHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinARes = await playerAHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player A"));
        Assert.True(joinARes.Success);
        var playerAId = joinARes.PlayerId!;

        await using var playerBHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerBHub.StartAsync();
        await playerBHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinBRes = await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player B"));
        Assert.True(joinBRes.Success);
        var playerBId = joinBRes.PlayerId!;

        // Track broadcasts
        PlayerTeamChangedEvent? lastEventForB = null;
        playerBHub.On<PlayerTeamChangedEvent>("PlayerTeamChanged", ev => lastEventForB = ev);

        // 4. Player A joins Red (capacity 1)
        var aJoinRedRes = await playerAHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerAId, redId));
        Assert.True(aJoinRedRes.Success);

        await Task.Delay(100);
        Assert.NotNull(lastEventForB);
        Assert.Equal(playerAId, lastEventForB.PlayerId);
        Assert.Null(lastEventForB.OldTeamId);
        Assert.Equal(redId, lastEventForB.NewTeamId);

        // 5. Player B tries to join Red -> FAILS because Red is full (1/1)
        var bJoinRedRes = await playerBHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerBId, redId));
        Assert.False(bJoinRedRes.Success);
        Assert.Equal(RoomErrorCodes.TeamFull, bJoinRedRes.ErrorCode);

        // 6. Player B joins Blue (capacity 2) -> SUCCESS
        var bJoinBlueRes = await playerBHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerBId, blueId));
        Assert.True(bJoinBlueRes.Success);

        await Task.Delay(100);
        Assert.Equal(playerBId, lastEventForB.PlayerId);
        Assert.Equal(blueId, lastEventForB.NewTeamId);

        // 7. Player A leaves Red -> SUCCESS
        var aLeaveRedRes = await playerAHub.InvokeAsync<LeaveTeamResponse>("LeaveTeam", new LeaveTeamRequest(roomId, playerAId));
        Assert.True(aLeaveRedRes.Success);

        await Task.Delay(100);
        Assert.Equal(playerAId, lastEventForB.PlayerId);
        Assert.Equal(redId, lastEventForB.OldTeamId);
        Assert.Null(lastEventForB.NewTeamId);

        // 8. Now Player B switches from Blue to Red -> SUCCESS (Red now has 0/1)
        var bSwitchRedRes = await playerBHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerBId, redId));
        Assert.True(bSwitchRedRes.Success);

        await Task.Delay(100);
        Assert.Equal(playerBId, lastEventForB.PlayerId);
        Assert.Equal(blueId, lastEventForB.OldTeamId);
        Assert.Equal(redId, lastEventForB.NewTeamId);
    }
}


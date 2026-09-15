using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class TeamIsolationIntegrationTests
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
    public async Task SignalR_TeamStateIsolation_PlayerCOnlyReceivesBlueTeam_AandBOnlyReceiveRedTeam()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room with Red and Blue teams
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Team Isolation", 900));
        Assert.True(createRes.Success);
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;
        var adminToken = createRes.AdminToken!;

        var redTeam = (await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đỏ", "#E53935", 2))).Team!;
        var blueTeam = (await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Xanh", "#1E88E5", 2))).Team!;

        // 2. Players A & B join Red Team, Player C joins Blue Team
        await using var playerAHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerAHub.StartAsync();
        await playerAHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinA = await playerAHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player A"));
        var pAId = joinA.PlayerId!;
        await playerAHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, pAId, redTeam.TeamId));
        await playerAHub.InvokeAsync<SelectAvatarResponse>("SelectAvatar", new SelectAvatarRequest(roomId, pAId, "bao"));
        await playerAHub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, pAId, true));

        await using var playerBHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerBHub.StartAsync();
        await playerBHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinB = await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player B"));
        var pBId = joinB.PlayerId!;
        await playerBHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, pBId, redTeam.TeamId));
        await playerBHub.InvokeAsync<SelectAvatarResponse>("SelectAvatar", new SelectAvatarRequest(roomId, pBId, "dung"));
        await playerBHub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, pBId, true));

        await using var playerCHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerCHub.StartAsync();
        await playerCHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinC = await playerCHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player C"));
        var pCId = joinC.PlayerId!;
        await playerCHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, pCId, blueTeam.TeamId));
        await playerCHub.InvokeAsync<SelectAvatarResponse>("SelectAvatar", new SelectAvatarRequest(roomId, pCId, "phuong"));
        await playerCHub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, pCId, true));

        // 3. Register TeamStateUpdated listeners
        var aTeamSnapshots = new List<TeamGameStateSnapshot>();
        var bTeamSnapshots = new List<TeamGameStateSnapshot>();
        var cTeamSnapshots = new List<TeamGameStateSnapshot>();

        var aStateTcs = new TaskCompletionSource<TeamGameStateSnapshot>();
        var bStateTcs = new TaskCompletionSource<TeamGameStateSnapshot>();
        var cStateTcs = new TaskCompletionSource<TeamGameStateSnapshot>();

        playerAHub.On<TeamGameStateSnapshot>("TeamStateUpdated", snap =>
        {
            aTeamSnapshots.Add(snap);
            aStateTcs.TrySetResult(snap);
        });

        playerBHub.On<TeamGameStateSnapshot>("TeamStateUpdated", snap =>
        {
            bTeamSnapshots.Add(snap);
            bStateTcs.TrySetResult(snap);
        });

        playerCHub.On<TeamGameStateSnapshot>("TeamStateUpdated", snap =>
        {
            cTeamSnapshots.Add(snap);
            cStateTcs.TrySetResult(snap);
        });

        // 4. Admin starts match with 1s countdown
        var startRes = await adminHub.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch",
            new AdminStartMatchRequest(roomId, adminToken, 1));
        Assert.True(startRes.Success);

        // Wait for match to advance to Playing and broadcast team state
        var aSnap = await aStateTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bSnap = await bStateTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cSnap = await cStateTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 5. Assert isolation on Player A and B (Red Team)
        Assert.Equal(redTeam.TeamId, aSnap.TeamId);
        Assert.Equal(redTeam.TeamId, bSnap.TeamId);
        Assert.Equal(2, aSnap.Members.Count);
        Assert.Contains(aSnap.Members, m => m.PlayerId == pAId && m.DisplayName == "Player A");
        Assert.Contains(aSnap.Members, m => m.PlayerId == pBId && m.DisplayName == "Player B");
        Assert.DoesNotContain(aSnap.Members, m => m.PlayerId == pCId);

        // 6. Assert isolation on Player C (Blue Team)
        Assert.Equal(blueTeam.TeamId, cSnap.TeamId);
        Assert.Single(cSnap.Members);
        Assert.Contains(cSnap.Members, m => m.PlayerId == pCId && m.DisplayName == "Player C");
        Assert.DoesNotContain(cSnap.Members, m => m.PlayerId == pAId);
        Assert.DoesNotContain(cSnap.Members, m => m.PlayerId == pBId);

        // 7. Verify GetMyTeamState request returns only caller's team
        var aQueryRes = await playerAHub.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, pAId));
        Assert.True(aQueryRes.Success);
        Assert.Equal(redTeam.TeamId, aQueryRes.State!.TeamId);
        Assert.DoesNotContain(aQueryRes.State.Members, m => m.PlayerId == pCId);

        var cQueryRes = await playerCHub.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, pCId));
        Assert.True(cQueryRes.Success);
        Assert.Equal(blueTeam.TeamId, cQueryRes.State!.TeamId);
        Assert.DoesNotContain(cQueryRes.State.Members, m => m.PlayerId == pAId);
        Assert.DoesNotContain(cQueryRes.State.Members, m => m.PlayerId == pBId);
    }
}


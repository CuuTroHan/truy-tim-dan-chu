using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class MovementIntegrationTests
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
    public async Task SignalR_PlayerMovement_IsAuthoritative_AndBroadcastsOnlyToTeammates()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Setup room and teams
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Movement Test", 900));
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;
        var adminToken = createRes.AdminToken!;

        var redTeam = (await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đỏ", "#E53935", 2))).Team!;
        var blueTeam = (await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Xanh", "#1E88E5", 2))).Team!;

        // 2. Player A & B in Red Team, Player C in Blue Team
        await using var playerA = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerA.StartAsync();
        await playerA.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinA = await playerA.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player A"));
        var pAId = joinA.PlayerId!;
        await playerA.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, pAId, redTeam.TeamId));
        await playerA.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, pAId, true));

        await using var playerB = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerB.StartAsync();
        await playerB.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinB = await playerB.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player B"));
        var pBId = joinB.PlayerId!;
        await playerB.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, pBId, redTeam.TeamId));
        await playerB.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, pBId, true));

        await using var playerC = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerC.StartAsync();
        await playerC.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinC = await playerC.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player C"));
        var pCId = joinC.PlayerId!;
        await playerC.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, pCId, blueTeam.TeamId));
        await playerC.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, pCId, true));

        // Listen for PlayerMoved on B and C
        var bMovedTcs = new TaskCompletionSource<PlayerMovedBroadcast>();
        var cMovedList = new List<PlayerMovedBroadcast>();

        playerB.On<PlayerMovedBroadcast>("PlayerMoved", ev => bMovedTcs.TrySetResult(ev));
        playerC.On<PlayerMovedBroadcast>("PlayerMoved", ev => cMovedList.Add(ev));

        // 3. Start match (1s countdown)
        await adminHub.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch", new AdminStartMatchRequest(roomId, adminToken, 1));
        await Task.Delay(1500); // Wait for match to become Playing

        // Players fetch team state to join their team SignalR groups
        var aState = await playerA.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, pAId));
        Assert.True(aState.Success);
        var initialAX = aState.State!.Members.First(m => m.PlayerId == pAId).X;

        var bState = await playerB.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, pBId));
        Assert.True(bState.Success);

        var cState = await playerC.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, pCId));
        Assert.True(cState.Success);

        // 4. Player A moves Right (key 8, sequence 1)
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ack1 = await playerA.InvokeAsync<MovementAck>("SendMovement",
            new PlayerMovementInput(roomId, pAId, 8, 1, now));

        Assert.True(ack1.Success);
        Assert.Equal(1, ack1.Sequence);
        Assert.True(ack1.X > initialAX); // Moved right
        Assert.Equal(1, ack1.Facing); // Facing Right
        Assert.Equal(1, ack1.Walking);

        // 5. Player B (teammate) receives broadcast
        var bBroadcast = await bMovedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(pAId, bBroadcast.PlayerId);
        Assert.Equal(ack1.X, bBroadcast.X);
        Assert.Equal(ack1.Y, bBroadcast.Y);

        // 6. Player C (other team) receives NOTHING
        Assert.Empty(cMovedList);

        // 7. Test outdated sequence rejection
        var ackOld = await playerA.InvokeAsync<MovementAck>("SendMovement",
            new PlayerMovementInput(roomId, pAId, 8, 1, now + 50));
        Assert.False(ackOld.Success);
        Assert.Equal("OUTDATED_SEQUENCE", ackOld.ErrorCode);
    }
}


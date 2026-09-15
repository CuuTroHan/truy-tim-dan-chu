using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class ReadinessIntegrationTests
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
    public async Task SignalR_ReadyAndLocks_FullFlow()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Ready Test", 900));
        Assert.True(createRes.Success);
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;
        var adminToken = createRes.AdminToken!;

        // Add 2 teams: Red (Cap 2) and Blue (Cap 2)
        var redTeam = (await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đỏ", "#E53935", 2))).Team!;
        var blueTeam = (await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Xanh", "#1E88E5", 2))).Team!;

        // 2. Player A and Player B join
        await using var playerAHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerAHub.StartAsync();
        await playerAHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinARes = await playerAHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player A"));
        var playerAId = joinARes.PlayerId!;

        await using var playerBHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerBHub.StartAsync();
        await playerBHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinBRes = await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player B"));
        var playerBId = joinBRes.PlayerId!;

        // Players join teams
        await playerAHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerAId, redTeam.TeamId));
        await playerBHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerBId, blueTeam.TeamId));

        // 3. Player A readies up -> Player B receives broadcast
        var readyTcs = new TaskCompletionSource<PlayerReadyChangedEvent>();
        playerBHub.On<PlayerReadyChangedEvent>("PlayerReadyChanged", e => readyTcs.TrySetResult(e));

        var readyRes = await playerAHub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, playerAId, true));
        Assert.True(readyRes.Success);
        Assert.True(readyRes.IsReady);

        var readyEvt = await Task.WhenAny(readyTcs.Task, Task.Delay(2000));
        Assert.Same(readyTcs.Task, readyEvt);
        var readyResult = await readyTcs.Task;
        Assert.Equal(playerAId, readyResult.PlayerId);
        Assert.True(readyResult.IsReady);

        // 4. Admin locks join -> Player C fails to join
        var joinLockTcs = new TaskCompletionSource<bool>();
        playerAHub.On<bool>("JoinLockToggled", locked => joinLockTcs.TrySetResult(locked));

        var lockJoinRes = await adminHub.InvokeAsync<AdminSetJoinLockResponse>("AdminSetJoinLock",
            new AdminSetJoinLockRequest(roomId, adminToken, true));
        Assert.True(lockJoinRes.Success);
        Assert.True(lockJoinRes.IsJoinLocked);

        var joinLockEvt = await Task.WhenAny(joinLockTcs.Task, Task.Delay(2000));
        Assert.Same(joinLockTcs.Task, joinLockEvt);
        Assert.True(await joinLockTcs.Task);

        // Player C tries to join -> rejected with JOIN_LOCKED
        await using var playerCHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerCHub.StartAsync();
        await playerCHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinCRes = await playerCHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player C"));
        Assert.False(joinCRes.Success);
        Assert.Equal(RoomErrorCodes.JoinLocked, joinCRes.ErrorCode);

        // Admin unlocks join -> Player C can join
        await adminHub.InvokeAsync<AdminSetJoinLockResponse>("AdminSetJoinLock",
            new AdminSetJoinLockRequest(roomId, adminToken, false));
        var joinCSuccess = await playerCHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player C"));
        Assert.True(joinCSuccess.Success);

        // 5. Admin locks roster -> Player A fails to self-leave/switch team
        var rosterLockTcs = new TaskCompletionSource<bool>();
        var readyResetTcs = new TaskCompletionSource<string?>();
        playerAHub.On<bool>("RosterLockToggled", locked => rosterLockTcs.TrySetResult(locked));
        playerAHub.On<string?>("ReadyReset", reason => readyResetTcs.TrySetResult(reason));

        var lockRosterRes = await adminHub.InvokeAsync<AdminSetRosterLockResponse>("AdminSetRosterLock",
            new AdminSetRosterLockRequest(roomId, adminToken, true));
        Assert.True(lockRosterRes.Success);
        Assert.True(lockRosterRes.IsRosterLocked);

        var rosterEvt = await Task.WhenAny(rosterLockTcs.Task, Task.Delay(2000));
        Assert.Same(rosterLockTcs.Task, rosterEvt);
        Assert.True(await rosterLockTcs.Task);

        var resetEvt = await Task.WhenAny(readyResetTcs.Task, Task.Delay(2000));
        Assert.Same(readyResetTcs.Task, resetEvt);

        // Player A tries to switch team -> rejected with ROSTER_LOCKED
        var switchRes = await playerAHub.InvokeAsync<JoinTeamResponse>("JoinTeam",
            new JoinTeamRequest(roomId, playerAId, blueTeam.TeamId));
        Assert.False(switchRes.Success);
        Assert.Equal(RoomErrorCodes.RosterLocked, switchRes.ErrorCode);
    }
}

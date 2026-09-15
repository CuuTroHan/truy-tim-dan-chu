using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class CountdownIntegrationTests
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
    public async Task SignalR_CountdownAndMatchStart_FullFlow()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Countdown Test", 900));
        Assert.True(createRes.Success);
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;
        var adminToken = createRes.AdminToken!;

        // Add 2 teams
        var redTeam = (await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đỏ", "#E53935", 2))).Team!;
        var blueTeam = (await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Xanh", "#1E88E5", 2))).Team!;

        // 2. Player A and Player B join and set ready
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

        await playerAHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerAId, redTeam.TeamId));
        await playerBHub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerBId, blueTeam.TeamId));

        await playerAHub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, playerAId, true));
        await playerBHub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, playerBId, true));

        // 3. Setup event listeners
        var aCountdownTcs = new TaskCompletionSource<CountdownStartedEvent>();
        var bCountdownTcs = new TaskCompletionSource<CountdownStartedEvent>();
        var aCanceledTcs = new TaskCompletionSource<bool>();
        var bCanceledTcs = new TaskCompletionSource<bool>();
        var aMatchStartedTcs = new TaskCompletionSource<MatchStartedEvent>();
        var bMatchStartedTcs = new TaskCompletionSource<MatchStartedEvent>();

        playerAHub.On<CountdownStartedEvent>("CountdownStarted", ev => aCountdownTcs.TrySetResult(ev));
        playerBHub.On<CountdownStartedEvent>("CountdownStarted", ev => bCountdownTcs.TrySetResult(ev));
        playerAHub.On<CountdownCanceledEvent>("CountdownCanceled", _ => aCanceledTcs.TrySetResult(true));
        playerBHub.On<CountdownCanceledEvent>("CountdownCanceled", _ => bCanceledTcs.TrySetResult(true));
        playerAHub.On<MatchStartedEvent>("MatchStarted", ev => aMatchStartedTcs.TrySetResult(ev));
        playerBHub.On<MatchStartedEvent>("MatchStarted", ev => bMatchStartedTcs.TrySetResult(ev));

        // 4. Admin starts countdown
        var startRes1 = await adminHub.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch",
            new AdminStartMatchRequest(roomId, adminToken, 3));
        Assert.True(startRes1.Success);
        Assert.NotNull(startRes1.MatchId);

        var aCountdown = await aCountdownTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var bCountdown = await bCountdownTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(startRes1.MatchId, aCountdown.MatchId);
        Assert.Equal(startRes1.MatchId, bCountdown.MatchId);
        Assert.Equal(aCountdown.MatchStartTimeUtc, bCountdown.MatchStartTimeUtc);

        // 5. Player A tries to mutate avatar during countdown -> rejected
        var avatarRes = await playerAHub.InvokeAsync<SelectAvatarResponse>("SelectAvatar",
            new SelectAvatarRequest(roomId, playerAId, "han"));
        Assert.False(avatarRes.Success);

        // 6. Admin cancels countdown
        var cancelRes = await adminHub.InvokeAsync<AdminCancelCountdownResponse>("AdminCancelCountdown",
            new AdminCancelCountdownRequest(roomId, adminToken));
        Assert.True(cancelRes.Success);

        Assert.True(await aCanceledTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(await bCanceledTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)));

        // 7. Admin starts countdown again with 1 second -> should auto start match
        var startRes2 = await adminHub.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch",
            new AdminStartMatchRequest(roomId, adminToken, 1));
        Assert.True(startRes2.Success);

        // Both players receive MatchStarted event
        var aMatch = await aMatchStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bMatch = await bMatchStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(startRes2.MatchId, aMatch.MatchId);
        Assert.Equal(startRes2.MatchId, bMatch.MatchId);
    }
}


using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class NpcProgressIntegrationTests
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
    public async Task SignalR_NpcInteraction_UpdatesTeamState_AndIsolatesAcrossTeams()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin setup
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var createRoom = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng NPC Test", 900));
        var roomId = createRoom.RoomId!;
        var roomCode = createRoom.RoomCode!;
        var adminToken = createRoom.AdminToken!;

        var addRed = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam", new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 4));
        var addBlue = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam", new AdminAddTeamRequest(roomId, adminToken, "Đội Xanh", "#1E88E5", 4));
        var redTeam = addRed.Team!;
        var blueTeam = addBlue.Team!;

        // 2. Players connect and join teams
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

        // Setup listeners for TeamStateUpdated
        var bUpdatedTcs = new TaskCompletionSource<TeamGameStateSnapshot>();
        var cUpdatedList = new List<TeamGameStateSnapshot>();

        playerB.On<TeamGameStateSnapshot>("TeamStateUpdated", snap => bUpdatedTcs.TrySetResult(snap));
        playerC.On<TeamGameStateSnapshot>("TeamStateUpdated", snap => cUpdatedList.Add(snap));

        // 3. Start match
        await adminHub.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch", new AdminStartMatchRequest(roomId, adminToken, 1));
        await Task.Delay(1500);

        // All players fetch state to join their SignalR groups
        var aInitial = await playerA.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, pAId));
        Assert.True(aInitial.Success);
        var bInitial = await playerB.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, pBId));
        Assert.True(bInitial.Success);
        var cInitial = await playerC.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, pCId));
        Assert.True(cInitial.Success);

        // 4. Test Out-of-range interaction: Player A stands at spawn (~500, 366), tries to interact with Phuong at (170, 280)
        var farInteract = await playerA.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, pAId, "phuong", "cmd_far"));
        Assert.False(farInteract.Success);
        Assert.Equal(InteractErrorCodes.OutOfRange, farInteract.ErrorCode);

        // 5. Player A moves near Trong (526, 300) to trigger Opening -> Lights
        var roomManager = app.Services.GetRequiredService<RoomManager>();
        var room = roomManager.GetRoomById(roomId)!;
        var redGame = room.GetTeamGame(redTeam.TeamId)!;
        redGame.UpdateMemberPosition(pAId, 525f, 302f, 0, 0);

        // Player A interacts with Trong
        var openInteract = await playerA.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, pAId, "trong", "cmd_start_game"));
        Assert.True(openInteract.Success);
        Assert.True(openInteract.Mutated);
        Assert.Equal(Chapter.Lights, openInteract.State!.Progress.Chapter);

        // 6. Player A moves near Phuong (170, 280), Player B moves near Dung (205, 338)
        redGame.UpdateMemberPosition(pAId, 172f, 280f, 0, 0);
        redGame.UpdateMemberPosition(pBId, 206f, 338f, 0, 0);

        // Reset TCS for B
        bUpdatedTcs = new TaskCompletionSource<TeamGameStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        playerB.On<TeamGameStateSnapshot>("TeamStateUpdated", snap =>
        {
            if (snap.Progress.Spoken[0]) bUpdatedTcs.TrySetResult(snap);
        });

        // Player A interacts with Phuong
        var phuongInteract = await playerA.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, pAId, "phuong", "cmd_meet_phuong"));
        Assert.True(phuongInteract.Success);
        Assert.True(phuongInteract.Mutated);
        Assert.True(phuongInteract.State!.Progress.Spoken[0]);

        // Player B receives broadcast with Spoken[0] == true
        var bSnap = await bUpdatedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(bSnap.Progress.Spoken[0]);

        // 7. Player B interacts with Dung
        var dungInteract = await playerB.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, pBId, "dung", "cmd_meet_dung"));
        Assert.True(dungInteract.Success);
        Assert.True(dungInteract.Mutated);
        // Team progress now has BOTH flags: Spoken[0] and Spoken[1]
        Assert.True(dungInteract.State!.Progress.Spoken[0]);
        Assert.True(dungInteract.State!.Progress.Spoken[1]);

        // 8. Idempotent test: Player A interacts with Phuong AGAIN -> Success but NO new mutation
        var repeatInteract = await playerA.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, pAId, "phuong", "cmd_repeat"));
        Assert.True(repeatInteract.Success);
        Assert.False(repeatInteract.Mutated);

        // 9. Team isolation check: Player C received ZERO updates from Red Team and no Spoken flags
        Assert.DoesNotContain(cUpdatedList, snap => snap.TeamId == redTeam.TeamId);
        Assert.DoesNotContain(cUpdatedList, snap => snap.Progress.Spoken[0]);
    }
}

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

public sealed class ReservationIntegrationTests
{
    [Fact]
    public async Task SignalR_Reservation_IsExclusive_Releases_Expires_AndIsolatesTeams()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);
        await using var admin = await Connect(uri);
        var created = await admin.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng giữ quyền", 900));
        var roomId = created.RoomId!;
        var red = (await admin.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, created.AdminToken!, "Đỏ", "#E53935", 3))).Team!;
        var blue = (await admin.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, created.AdminToken!, "Xanh", "#1E88E5", 3))).Team!;

        var (a, aId) = await Join(uri, created.RoomCode!, roomId, red.TeamId, "An");
        await using var playerA = a;
        var (b, bId) = await Join(uri, created.RoomCode!, roomId, red.TeamId, "Bình");
        await using var playerB = b;
        var (c, cId) = await Join(uri, created.RoomCode!, roomId, blue.TeamId, "Chi");
        await using var playerC = c;

        await admin.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch", new AdminStartMatchRequest(roomId, created.AdminToken!, 1));
        await Task.Delay(1200);
        await playerA.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, aId));
        await playerB.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, bId));
        await playerC.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, cId));

        var room = app.Services.GetRequiredService<RoomManager>().GetRoomById(roomId)!;
        PrepareMirror(room.GetTeamGame(red.TeamId)!, aId, bId);
        PrepareMirror(room.GetTeamGame(blue.TeamId)!, cId);

        var barrier = new Barrier(2);
        var reserveA = Task.Run(async () => { barrier.SignalAndWait(); return await playerA.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle", new ReservePuzzleRequest(roomId, aId, PuzzleIds.Mirrors)); });
        var reserveB = Task.Run(async () => { barrier.SignalAndWait(); return await playerB.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle", new ReservePuzzleRequest(roomId, bId, PuzzleIds.Mirrors)); });
        var redResults = await Task.WhenAll(reserveA, reserveB);
        Assert.Single(redResults, x => x.Success);
        Assert.Single(redResults, x => x.ErrorCode == PuzzleReservationErrorCodes.PuzzleOccupied);

        var ownerHub = redResults[0].Success ? playerA : playerB;
        var ownerId = redResults[0].Success ? aId : bId;
        var otherHub = redResults[0].Success ? playerB : playerA;
        var otherId = redResults[0].Success ? bId : aId;
        var token = redResults.Single(x => x.Success).ReservationToken!;

        var forged = await otherHub.InvokeAsync<ReleasePuzzleResponse>("ReleasePuzzle",
            new ReleasePuzzleRequest(roomId, otherId, PuzzleIds.Mirrors, token));
        Assert.False(forged.Success);
        Assert.Equal(PuzzleReservationErrorCodes.NotReservationOwner, forged.ErrorCode);
        Assert.True((await ownerHub.InvokeAsync<ValidatePuzzleSubmissionResponse>("ValidatePuzzleSubmission",
            new ValidatePuzzleSubmissionRequest(roomId, ownerId, PuzzleIds.Mirrors, token))).Success);

        var blueReservation = await playerC.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, cId, PuzzleIds.Mirrors));
        Assert.True(blueReservation.Success);

        var released = await ownerHub.InvokeAsync<ReleasePuzzleResponse>("ReleasePuzzle",
            new ReleasePuzzleRequest(roomId, ownerId, PuzzleIds.Mirrors, token));
        Assert.True(released.Success);
        Assert.True((await otherHub.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, otherId, PuzzleIds.Mirrors))).Success);

        var disconnectReleased = new TaskCompletionSource<PuzzleReservationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        ownerHub.On<PuzzleReservationState>("PuzzleReservationChanged", state =>
        {
            if (!state.IsReserved && state.TeamId == red.TeamId)
                disconnectReleased.TrySetResult(state);
        });
        await otherHub.StopAsync();
        var disconnectEvent = await disconnectReleased.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(disconnectEvent.IsReserved);
    }

    private static void PrepareMirror(TeamGameInstance game, params string[] playerIds)
    {
        game.Progress.Chapter = Chapter.Lights;
        Array.Fill(game.Progress.Lamps, true);
        foreach (var id in playerIds) game.UpdateMemberPosition(id, 195, 323, 0, 0);
    }

    private static async Task<(HubConnection Hub, string PlayerId)> Join(Uri uri, string code, string roomId, string teamId, string name)
    {
        var hub = await Connect(uri);
        var joined = await hub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(code, name));
        await hub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, joined.PlayerId!, teamId));
        await hub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, joined.PlayerId!, true));
        return (hub, joined.PlayerId!);
    }

    private static async Task<HubConnection> Connect(Uri uri)
    {
        var hub = new HubConnectionBuilder().WithUrl(uri).Build();
        await hub.StartAsync();
        await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        return hub;
    }

    private static async Task<WebApplication> StartServer()
    {
        var app = ServerBootstrap.Build([
            "--urls", "http://127.0.0.1:0",
            "--Multiplayer:AllowedOrigins:0", "http://localhost:5267",
            "--Logging:LogLevel:Default", "Warning"]);
        await app.StartAsync();
        return app;
    }

    private static Uri HubUri(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new Uri(address + ConnectionProtocol.HubPath);
    }
}

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

public sealed class MirrorsIntegrationTests
{
    [Fact]
    public async Task SignalR_Mirrors_ValidatesOwner_Replay_ProgressTimingRelease_AndTeamIsolation()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);
        await using var admin = await Connect(uri);
        var created = await admin.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Gương", 900));
        var roomId = created.RoomId!;
        var red = (await admin.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, created.AdminToken!, "Đỏ", "#E53935", 3))).Team!;
        var blue = (await admin.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, created.AdminToken!, "Xanh", "#1E88E5", 2))).Team!;

        var (playerA, aId) = await Join(uri, created.RoomCode!, roomId, red.TeamId, "An");
        await using var a = playerA;
        var (playerB, bId) = await Join(uri, created.RoomCode!, roomId, red.TeamId, "Bình");
        await using var b = playerB;
        var (playerC, cId) = await Join(uri, created.RoomCode!, roomId, blue.TeamId, "Chi");
        await using var c = playerC;

        await admin.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch",
            new AdminStartMatchRequest(roomId, created.AdminToken!, 1));
        await Task.Delay(1200);
        await a.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, aId));
        await b.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, bId));
        await c.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, cId));

        var room = app.Services.GetRequiredService<RoomManager>().GetRoomById(roomId)!;
        var redGame = room.GetTeamGame(red.TeamId)!;
        var blueGame = room.GetTeamGame(blue.TeamId)!;
        PrepareMirror(redGame, aId, bId);
        PrepareMirror(blueGame, cId);

        var blueUpdates = new List<TeamGameStateSnapshot>();
        c.On<TeamGameStateSnapshot>("TeamStateUpdated", state => blueUpdates.Add(state));
        var reservation = await a.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, aId, PuzzleIds.Mirrors));
        Assert.True(reservation.Success);

        var stolen = await b.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, bId, PuzzleIds.Mirrors, reservation.ReservationToken!,
                [1, 2, 3, 0], "stolen"));
        Assert.False(stolen.Success);
        Assert.Equal(PuzzleReservationErrorCodes.NotReservationOwner, stolen.ErrorCode);

        var beforeMalformed = redGame.Progress.Version;
        var malformed = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Mirrors, reservation.ReservationToken!,
                [1, 2, 3], "malformed"));
        Assert.False(malformed.Success);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidAnswerPayload, malformed.ErrorCode);
        Assert.Equal(beforeMalformed, redGame.Progress.Version);

        var wrongUpdate = NextTeamUpdate(b, red.TeamId, state => state.Progress.WrongAnswerCount == 1);
        var wrong = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Mirrors, reservation.ReservationToken!,
                [0, 0, 0, 0], "wrong"));
        Assert.True(wrong.Success);
        Assert.False(wrong.Correct);
        Assert.Equal(1, (await wrongUpdate).Progress.WrongAnswerCount);

        var replayWrong = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Mirrors, reservation.ReservationToken!,
                [1, 2, 3, 0], "wrong"));
        Assert.True(replayWrong.Success);
        Assert.False(replayWrong.Correct);
        Assert.False(replayWrong.Mutated);
        Assert.Equal(1, redGame.Progress.WrongAnswerCount);

        var draftUpdate = NextTeamUpdate(b, red.TeamId, state => state.Progress.Chapter == Chapter.Draft);
        var released = NextReservationUpdate(b, red.TeamId, state => !state.IsReserved);
        var correct = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Mirrors, reservation.ReservationToken!,
                [1, 2, 3, 0], "correct"));

        Assert.True(correct.Success);
        Assert.True(correct.Correct);
        var teamState = await draftUpdate;
        Assert.Equal(Chapter.Draft, teamState.Progress.Chapter);
        Assert.Equal(1, teamState.Progress.Shards);
        Assert.Single(teamState.Progress.ChapterTimings!);
        Assert.False((await released).IsReserved);

        var staleToken = await a.InvokeAsync<ValidatePuzzleSubmissionResponse>("ValidatePuzzleSubmission",
            new ValidatePuzzleSubmissionRequest(roomId, aId, PuzzleIds.Mirrors, reservation.ReservationToken!));
        Assert.False(staleToken.Success);

        var replayCorrect = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Mirrors, reservation.ReservationToken!,
                [0, 0, 0, 0], "correct"));
        Assert.True(replayCorrect.Success);
        Assert.True(replayCorrect.Correct);
        Assert.False(replayCorrect.Mutated);
        Assert.Single(redGame.Progress.ChapterTimings);

        Assert.Equal(Chapter.Lights, blueGame.Progress.Chapter);
        Assert.Equal(0, blueGame.Progress.WrongAnswerCount);
        Assert.DoesNotContain(blueUpdates, state => state.TeamId == red.TeamId);
        Assert.True((await c.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, cId, PuzzleIds.Mirrors))).Success);
    }

    private static Task<TeamGameStateSnapshot> NextTeamUpdate(HubConnection hub, string teamId,
        Func<TeamGameStateSnapshot, bool> predicate)
    {
        var source = new TaskCompletionSource<TeamGameStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<TeamGameStateSnapshot>("TeamStateUpdated", state =>
        {
            if (state.TeamId == teamId && predicate(state)) source.TrySetResult(state);
        });
        return source.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static Task<PuzzleReservationState> NextReservationUpdate(HubConnection hub, string teamId,
        Func<PuzzleReservationState, bool> predicate)
    {
        var source = new TaskCompletionSource<PuzzleReservationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<PuzzleReservationState>("PuzzleReservationChanged", state =>
        {
            if (state.TeamId == teamId && predicate(state)) source.TrySetResult(state);
        });
        return source.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static void PrepareMirror(TeamGameInstance game, params string[] playerIds)
    {
        game.Progress.Chapter = Chapter.Lights;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        foreach (var id in playerIds) game.UpdateMemberPosition(id, 195, 323, 0, 0);
    }

    private static async Task<(HubConnection Hub, string PlayerId)> Join(Uri uri, string code, string roomId,
        string teamId, string name)
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

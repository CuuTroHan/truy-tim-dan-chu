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

public sealed class DraftIntegrationTests
{
    [Fact]
    public async Task SignalR_Draft_ValidatesOwner_Replay_ProgressTimingRelease_AndTeamIsolation()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);
        await using var admin = await Connect(uri);
        var created = await admin.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Dự thảo", 900));
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
        PrepareDraft(redGame, aId, bId);
        PrepareDraft(blueGame, cId);

        var blueUpdates = new List<TeamGameStateSnapshot>();
        c.On<TeamGameStateSnapshot>("TeamStateUpdated", state => blueUpdates.Add(state));

        // 1. Submit đúng nhưng không có reservation bị từ chối
        var noLock = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Draft, "no-lock-token",
                [0, 1, 2, 3, 4, 5], "no-lock"));
        Assert.False(noLock.Success);
        Assert.Equal(PuzzleReservationErrorCodes.ReservationExpired, noLock.ErrorCode);

        // 2. A reserve Draft; B nhận event reservation
        var bReceivedReservation = NextReservationUpdate(b, red.TeamId, state => state.IsReserved && state.OwnerPlayerId == aId && state.PuzzleId == PuzzleIds.Draft);
        var reservation = await a.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, aId, PuzzleIds.Draft));
        Assert.True(reservation.Success);
        Assert.NotNull(reservation.ReservationToken);
        var bEvent = await bReceivedReservation;
        Assert.True(bEvent.IsReserved);
        Assert.Equal(aId, bEvent.OwnerPlayerId);
        Assert.Equal(PuzzleIds.Draft, bEvent.PuzzleId);

        // 3. B dùng token của A submit bị NOT_RESERVATION_OWNER
        var stolen = await b.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, bId, PuzzleIds.Draft, reservation.ReservationToken!,
                [0, 1, 2, 3, 4, 5], "stolen"));
        Assert.False(stolen.Success);
        Assert.Equal(PuzzleReservationErrorCodes.NotReservationOwner, stolen.ErrorCode);

        // 4. A gửi payload malformed (thiếu phần tử): không mutation
        var beforeMalformed = redGame.Progress.Version;
        var malformed = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Draft, reservation.ReservationToken!,
                [0, 1, 2, 3, 4], "malformed"));
        Assert.False(malformed.Success);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidAnswerPayload, malformed.ErrorCode);
        Assert.Equal(beforeMalformed, redGame.Progress.Version);

        // 5. A gửi permutation sai: B nhận TeamStateUpdated, WrongAnswerCount và DraftFailures tăng 1, chapter vẫn Draft
        var wrongUpdate = NextTeamUpdate(b, red.TeamId, state => state.Progress.WrongAnswerCount == 1);
        var wrong = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Draft, reservation.ReservationToken!,
                [2, 0, 5, 1, 4, 3], "wrong"));
        Assert.True(wrong.Success);
        Assert.False(wrong.Correct);
        var updatedState = await wrongUpdate;
        Assert.Equal(1, updatedState.Progress.WrongAnswerCount);
        Assert.Equal(1, updatedState.Progress.DraftFailures);
        Assert.Equal(Chapter.Draft, updatedState.Progress.Chapter);

        // 6. Replay command sai với đáp án đúng: không tăng lỗi, không sang River
        var replayWrong = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Draft, reservation.ReservationToken!,
                [0, 1, 2, 3, 4, 5], "wrong"));
        Assert.True(replayWrong.Success);
        Assert.False(replayWrong.Correct);
        Assert.False(replayWrong.Mutated);
        Assert.Equal(1, redGame.Progress.WrongAnswerCount);
        Assert.Equal(1, redGame.Progress.DraftFailures);
        Assert.Equal(Chapter.Draft, redGame.Progress.Chapter);

        // 7. A gửi command mới với thứ tự đúng:
        //    A/B nhận River, 2 mảnh, DraftDone=true, có timing Lights và timing Draft, B nhận release reservation
        var riverUpdate = NextTeamUpdate(b, red.TeamId, state => state.Progress.Chapter == Chapter.River);
        var released = NextReservationUpdate(b, red.TeamId, state => !state.IsReserved && state.PuzzleId == PuzzleIds.Draft);
        var correct = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Draft, reservation.ReservationToken!,
                [0, 1, 2, 3, 4, 5], "correct"));

        Assert.True(correct.Success);
        Assert.True(correct.Correct);
        var teamState = await riverUpdate;
        Assert.Equal(Chapter.River, teamState.Progress.Chapter);
        Assert.Equal(2, teamState.Progress.Shards);
        Assert.True(teamState.Progress.DraftDone);
        Assert.Equal(2, teamState.Progress.ChapterTimings!.Count);
        Assert.Contains(teamState.Progress.ChapterTimings, t => t.Chapter == Chapter.Lights);
        Assert.Contains(teamState.Progress.ChapterTimings, t => t.Chapter == Chapter.Draft);
        var releasedEvent = await released;
        Assert.False(releasedEvent.IsReserved);

        // 8. Token cũ không còn hợp lệ
        var staleToken = await a.InvokeAsync<ValidatePuzzleSubmissionResponse>("ValidatePuzzleSubmission",
            new ValidatePuzzleSubmissionRequest(roomId, aId, PuzzleIds.Draft, reservation.ReservationToken!));
        Assert.False(staleToken.Success);

        // 9. Replay command đúng sau release: success/correct, Mutated=false, không thêm timing
        var replayCorrect = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Draft, reservation.ReservationToken!,
                [2, 0, 5, 1, 4, 3], "correct"));
        Assert.True(replayCorrect.Success);
        Assert.True(replayCorrect.Correct);
        Assert.False(replayCorrect.Mutated);
        Assert.Equal(2, redGame.Progress.ChapterTimings.Count);

        // 10. Team isolation: Blue team vẫn Draft, 0 lỗi, C có thể reserve Draft độc lập
        Assert.Equal(Chapter.Draft, blueGame.Progress.Chapter);
        Assert.Equal(1, blueGame.Progress.Shards);
        Assert.Equal(0, blueGame.Progress.WrongAnswerCount);
        Assert.DoesNotContain(blueUpdates, state => state.TeamId == red.TeamId);
        Assert.True((await c.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, cId, PuzzleIds.Draft))).Success);
    }

    private static Task<TeamGameStateSnapshot> NextTeamUpdate(HubConnection hub, string teamId,
        Func<TeamGameStateSnapshot, bool> predicate)
    {
        var source = new TaskCompletionSource<TeamGameStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<TeamGameStateSnapshot>("TeamStateUpdated", state =>
        {
            if (state.TeamId == teamId && predicate(state)) source.TrySetResult(state);
        });
        return source.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Task<PuzzleReservationState> NextReservationUpdate(HubConnection hub, string teamId,
        Func<PuzzleReservationState, bool> predicate)
    {
        var source = new TaskCompletionSource<PuzzleReservationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<PuzzleReservationState>("PuzzleReservationChanged", state =>
        {
            if (state.TeamId == teamId && predicate(state)) source.TrySetResult(state);
        });
        return source.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void PrepareDraft(TeamGameInstance game, params string[] playerIds)
    {
        game.Progress.Chapter = Chapter.Draft;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Lights))
        {
            game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, DateTimeOffset.UtcNow.AddMinutes(-1), 60_000));
        }
        foreach (var id in playerIds) game.UpdateMemberPosition(id, 486, 115, 0, 0);
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

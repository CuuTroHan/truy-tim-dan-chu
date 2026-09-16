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

public sealed class NewsIntegrationTests
{
    [Fact]
    public async Task SignalR_News_ValidatesReservation_Prerequisites_Noise_FinaleTransition_AndTeamIsolation()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);
        await using var admin = await Connect(uri);
        var created = await admin.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Bảng tin", 900));
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
        PrepareNews(redGame, aId, bId);

        var blueUpdates = new List<TeamGameStateSnapshot>();
        c.On<TeamGameStateSnapshot>("TeamStateUpdated", state => blueUpdates.Add(state));

        // 1. A reserve News (news_board tại 246, 516)
        var bReceivedReservation = NextReservationUpdate(b, red.TeamId,
            state => state.IsReserved && state.OwnerPlayerId == aId && state.PuzzleId == PuzzleIds.News);
        var reservation = await a.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, aId, PuzzleIds.News));
        Assert.True(reservation.Success);
        Assert.NotNull(reservation.ReservationToken);
        var bResEvent = await bReceivedReservation;
        Assert.True(bResEvent.IsReserved);
        Assert.Equal(aId, bResEvent.OwnerPlayerId);

        // 2. B dùng token của A submit bị NOT_RESERVATION_OWNER
        var stolen = await b.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, bId, PuzzleIds.News, reservation.ReservationToken!,
                [2], "cmd-stolen"));
        Assert.False(stolen.Success);
        Assert.Equal(PuzzleReservationErrorCodes.NotReservationOwner, stolen.ErrorCode);

        // 3. A submit choice 2 sớm khi chưa đủ clue -> PREREQUISITE_NOT_MET, không tính lỗi
        var earlyChoice2 = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.News, reservation.ReservationToken!,
                [2], "cmd-early-2"));
        Assert.False(earlyChoice2.Success);
        Assert.Equal(PuzzleReservationErrorCodes.PrerequisiteNotMet, earlyChoice2.ErrorCode);
        Assert.Equal(0, redGame.Progress.WrongAnswerCount);
        Assert.Equal(Chapter.News, redGame.Progress.Chapter);

        // 4. Đồng đội thu thập đủ 3 manh mối
        redGame.UpdateMemberPosition(aId, 180, 520, 0, 0);
        await a.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, aId, "clue0", "cmd-c0"));
        redGame.UpdateMemberPosition(bId, 339, 509, 0, 0);
        await b.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, bId, "clue1", "cmd-c1"));
        redGame.UpdateMemberPosition(bId, 259, 537, 0, 0);
        await b.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, bId, "bao", "cmd-bao"));

        Assert.True(redGame.Progress.Clues[0]);
        Assert.True(redGame.Progress.Clues[1]);
        Assert.True(redGame.Progress.Clues[2]);

        // 5. A submit choice 0: gây nhiễu, tăng lỗi, B nhận TeamStateUpdated có NewsNoise=1 và WrongAnswerCount=1
        redGame.UpdateMemberPosition(aId, 246, 516, 0, 0);
        var bNoiseUpdate = NextTeamUpdate(b, red.TeamId, s => s.Progress.NewsNoise == 1 && s.Progress.WrongAnswerCount == 1);
        var choice0Res = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.News, reservation.ReservationToken!,
                [0], "cmd-choice-0"));
        Assert.True(choice0Res.Success);
        Assert.False(choice0Res.Correct);
        var bNoiseState = await bNoiseUpdate;
        Assert.Equal(1, bNoiseState.Progress.NewsNoise);
        Assert.Equal(1, bNoiseState.Progress.WrongAnswerCount);
        Assert.Equal(Chapter.News, bNoiseState.Progress.Chapter);

        // 6. A submit choice 2 đúng: chuyển sang Finale, 4 mảnh, thêm timing News, giải phóng reservation
        var bFinaleUpdate = NextTeamUpdate(b, red.TeamId, s => s.Progress.Chapter == Chapter.Finale);
        var bReleased = NextReservationUpdate(b, red.TeamId, s => !s.IsReserved && s.PuzzleId == PuzzleIds.News);
        var choice2Res = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.News, reservation.ReservationToken!,
                [2], "cmd-choice-2"));
        Assert.True(choice2Res.Success);
        Assert.True(choice2Res.Correct);

        var bFinaleState = await bFinaleUpdate;
        Assert.Equal(Chapter.Finale, bFinaleState.Progress.Chapter);
        Assert.Equal(4, bFinaleState.Progress.Shards);
        Assert.True(bFinaleState.Progress.NewsDone);
        Assert.Equal(0, bFinaleState.Progress.NewsNoise);
        Assert.Null(bFinaleState.Progress.FinishedAtUtc);
        Assert.False(bFinaleState.Progress.FinaleDone);
        Assert.Equal(4, bFinaleState.Progress.ChapterTimings?.Count ?? 0);

        var releasedEvent = await bReleased;
        Assert.False(releasedEvent.IsReserved);

        // 7. Team isolation: Đội Xanh không nhận broadcast của Đội Đỏ và vẫn ở Opening (0/4 mảnh)
        Assert.DoesNotContain(blueUpdates, state => state.TeamId == red.TeamId);
        Assert.Equal(Chapter.Opening, blueGame.Progress.Chapter);
        Assert.Equal(0, blueGame.Progress.Shards);
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

    private static void PrepareNews(TeamGameInstance game, params string[] playerIds)
    {
        game.Progress.Chapter = Chapter.News;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        game.Progress.DraftDone = true;
        game.Progress.RiverDone = true;
        if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Lights))
        {
            game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, DateTimeOffset.UtcNow.AddMinutes(-3), 60_000));
        }
        if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Draft))
        {
            game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, DateTimeOffset.UtcNow.AddMinutes(-2), 60_000));
        }
        if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.River))
        {
            game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.River, DateTimeOffset.UtcNow.AddMinutes(-1), 60_000));
        }
        foreach (var id in playerIds) game.UpdateMemberPosition(id, 246, 516, 0, 0);
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

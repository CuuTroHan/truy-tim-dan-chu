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

public sealed class FinaleIntegrationTests
{
    [Fact]
    public async Task SignalR_Finale_TwoStages_Handover_Complete_TeamIsolation_AndRoomPlaying()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);
        await using var admin = await Connect(uri);
        var created = await admin.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Chung cuộc", 900));
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

        // Chuẩn bị Đội Đỏ ở Finale tại final_board (513, 328)
        PrepareFinale(redGame, aId, bId);

        var blueUpdates = new List<TeamGameStateSnapshot>();
        c.On<TeamGameStateSnapshot>("TeamStateUpdated", state => blueUpdates.Add(state));

        // Theo dõi sự kiện TeamUpdated gửi cho cả phòng khi đội hoàn thành
        var teamUpdatedTcs = new TaskCompletionSource<TeamSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        admin.On<TeamSnapshot>("TeamUpdated", snap =>
        {
            if (snap.TeamId == red.TeamId && snap.FinishedAtUtc.HasValue)
                teamUpdatedTcs.TrySetResult(snap);
        });

        // 1. A reserve Finale (final_board tại 513, 328), B thấy owner
        var bReceivedReservation = NextReservationUpdate(b, red.TeamId,
            state => state.IsReserved && state.OwnerPlayerId == aId && state.PuzzleId == PuzzleIds.Finale);
        var reservation = await a.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, aId, PuzzleIds.Finale));
        Assert.True(reservation.Success);
        Assert.NotNull(reservation.ReservationToken);
        var bResEvent = await bReceivedReservation;
        Assert.True(bResEvent.IsReserved);
        Assert.Equal(aId, bResEvent.OwnerPlayerId);

        // 2. A gửi order sai: [1, 0, 2, 3] -> lỗi tăng 1, vẫn Finale, chưa Finished
        var wrongOrderRes = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Finale, reservation.ReservationToken!,
                [1, 0, 2, 3], "cmd-wrong-order"));
        Assert.True(wrongOrderRes.Success);
        Assert.False(wrongOrderRes.Correct);
        Assert.Equal(1, redGame.Progress.WrongAnswerCount);
        Assert.False(redGame.Progress.FinaleOrderCompleted);
        Assert.Null(redGame.Progress.FinishedAtUtc);

        // 3. A gửi order đúng: [0, 1, 2, 3] -> FinaleOrderCompleted = true, ReturnStep = 0, reservation KHÔNG bị release
        var bOrderUpdate = NextTeamUpdate(b, red.TeamId, s => s.Progress.FinaleOrderCompleted);
        var correctOrderRes = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Finale, reservation.ReservationToken!,
                [0, 1, 2, 3], "cmd-correct-order"));
        Assert.True(correctOrderRes.Success);
        Assert.True(correctOrderRes.Correct);

        var bOrderState = await bOrderUpdate;
        Assert.True(bOrderState.Progress.FinaleOrderCompleted);
        Assert.Equal(0, bOrderState.Progress.ReturnStep);
        Assert.Equal(Chapter.Finale, bOrderState.Progress.Chapter);
        Assert.Null(bOrderState.Progress.FinishedAtUtc);
        Assert.False(bOrderState.Progress.FinaleDone);

        // 4. A hoàn thành step 0: [0] -> ReturnStep = 1, reservation vẫn giữ
        var bStep0Update = NextTeamUpdate(b, red.TeamId, s => s.Progress.ReturnStep == 1);
        var step0Res = await a.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, aId, PuzzleIds.Finale, reservation.ReservationToken!,
                [0], "cmd-step-0"));
        Assert.True(step0Res.Success);
        Assert.True(step0Res.Correct);
        var bStep0State = await bStep0Update;
        Assert.Equal(1, bStep0State.Progress.ReturnStep);

        // 5. A giải phóng reservation (mô phỏng nhường quyền hoặc đổi người giải)
        var bReleased = NextReservationUpdate(b, red.TeamId, s => !s.IsReserved && s.PuzzleId == PuzzleIds.Finale);
        var releaseRes = await a.InvokeAsync<ReleasePuzzleResponse>("ReleasePuzzle",
            new ReleasePuzzleRequest(roomId, aId, PuzzleIds.Finale, reservation.ReservationToken!));
        Assert.True(releaseRes.Success);
        await bReleased;

        // 6. B giữ quyền Finale tiếp tục từ step 1
        var bReserveRes = await b.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, bId, PuzzleIds.Finale));
        Assert.True(bReserveRes.Success);
        Assert.NotNull(bReserveRes.ReservationToken);

        // B thử bỏ bước (gửi step 2 thay vì step 1) -> INVALID_RETURN_STEP
        var skipRes = await b.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, bId, PuzzleIds.Finale, bReserveRes.ReservationToken!,
                [2], "cmd-b-skip"));
        Assert.False(skipRes.Success);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidReturnStep, skipRes.ErrorCode);
        Assert.Equal(1, redGame.Progress.ReturnStep);

        // 7. B thực hiện tuần tự step 1 -> step 2 -> step 3
        var step1Res = await b.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, bId, PuzzleIds.Finale, bReserveRes.ReservationToken!,
                [1], "cmd-b-step1"));
        Assert.True(step1Res.Success);

        var step2Res = await b.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, bId, PuzzleIds.Finale, bReserveRes.ReservationToken!,
                [2], "cmd-b-step2"));
        Assert.True(step2Res.Success);

        // Step 3 hoàn tất toàn bộ game: Complete, FinishedAtUtc, release reservation, TeamUpdated
        var aCompleteUpdate = NextTeamUpdate(a, red.TeamId, s => s.Progress.Chapter == Chapter.Complete);
        var bFinaleReleased = NextReservationUpdate(a, red.TeamId, s => !s.IsReserved && s.PuzzleId == PuzzleIds.Finale);

        var step3Res = await b.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, bId, PuzzleIds.Finale, bReserveRes.ReservationToken!,
                [3], "cmd-b-step3"));
        Assert.True(step3Res.Success);
        Assert.True(step3Res.Correct);

        var aCompleteState = await aCompleteUpdate;
        Assert.Equal(Chapter.Complete, aCompleteState.Progress.Chapter);
        Assert.True(aCompleteState.Progress.FinaleDone);
        Assert.NotNull(aCompleteState.Progress.FinishedAtUtc);

        var finaleReleasedEvent = await bFinaleReleased;
        Assert.False(finaleReleasedEvent.IsReserved);

        // 8. TeamUpdated chứa FinishedAtUtc được gửi cho cả phòng
        var updatedTeam = await teamUpdatedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(updatedTeam.FinishedAtUtc);
        Assert.Equal(aCompleteState.Progress.FinishedAtUtc, updatedTeam.FinishedAtUtc);

        // 9. Lệnh mới của Đội Đỏ bị từ chối với TEAM_ALREADY_FINISHED
        var newAction = await a.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, aId, PuzzleIds.Finale));
        Assert.False(newAction.Success);
        Assert.Equal(PuzzleReservationErrorCodes.TeamAlreadyFinished, newAction.ErrorCode);

        // 10. Đội C (Đội Xanh) vẫn hoạt động bình thường (di chuyển được)
        var cMove = await c.InvokeAsync<MovementAck>("SendMovement",
            new PlayerMovementInput(roomId, cId, 8, 1, 1000));
        Assert.True(cMove.Success);
        Assert.Equal(Chapter.Opening, blueGame.Progress.Chapter);
        Assert.DoesNotContain(blueUpdates, state => state.TeamId == red.TeamId);

        // 11. Trạng thái phòng thi đấu vẫn là Playing (không kết thúc sớm)
        Assert.Equal(RoomStatus.Playing, room.Status);
    }

    private static void PrepareFinale(TeamGameInstance game, string aId, string bId)
    {
        game.Progress.Chapter = Chapter.Finale;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        Array.Fill(game.Progress.Clues, true);
        game.Progress.DraftDone = true;
        game.Progress.RiverDone = true;
        game.Progress.NewsDone = true;
        game.Progress.FinaleOrderCompleted = false;
        game.Progress.ReturnStep = 0;
        game.Progress.FinaleDone = false;
        game.Progress.FinishedAtUtc = null;
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, DateTimeOffset.UtcNow.AddMinutes(-4), 60_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, DateTimeOffset.UtcNow.AddMinutes(-3), 60_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.River, DateTimeOffset.UtcNow.AddMinutes(-2), 60_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.News, DateTimeOffset.UtcNow.AddMinutes(-1), 60_000));

        game.UpdateMemberPosition(aId, 513, 328, 0, 0);
        game.UpdateMemberPosition(bId, 513, 328, 0, 0);
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

    private static async Task<HubConnection> Connect(Uri uri)
    {
        var hub = new HubConnectionBuilder().WithUrl(uri).Build();
        await hub.StartAsync();
        await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        return hub;
    }

    private static async Task<(HubConnection Connection, string PlayerId)> Join(
        Uri uri, string roomCode, string roomId, string teamId, string name)
    {
        var hub = await Connect(uri);
        var joined = await hub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, name));
        Assert.True(joined.Success);
        var playerId = joined.PlayerId!;
        var assigned = await hub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, playerId, teamId));
        Assert.True(assigned.Success);
        var ready = await hub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, playerId, true));
        Assert.True(ready.Success);
        return (hub, playerId);
    }
}

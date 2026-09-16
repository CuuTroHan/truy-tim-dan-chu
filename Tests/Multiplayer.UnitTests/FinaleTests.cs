using System.Collections.Concurrent;
using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class FinaleTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    // 1. 4 mảnh nhưng chưa xếp đúng không Finished
    [Fact]
    public void ShardsAre4_BeforeOrderCompleted_TeamNotFinished()
    {
        var game = ReadyFinaleGame();

        Assert.Equal(Chapter.Finale, game.Progress.Chapter);
        Assert.Equal(4, game.Progress.Shards);
        Assert.False(game.Progress.FinaleOrderCompleted);
        Assert.Equal(0, game.Progress.ReturnStep);
        Assert.False(game.Progress.FinaleDone);
        Assert.Null(game.Progress.FinishedAtUtc);
    }

    // 2. Malformed order bị từ chối không tính lỗi
    [Theory]
    [InlineData(null)]
    [InlineData(new int[] { 0, 1, 2 })]
    [InlineData(new int[] { 0, 1, 2, 3, 4 })]
    [InlineData(new int[] { 0, 0, 1, 2 })]
    [InlineData(new int[] { 0, 1, 2, 9 })]
    public void MalformedOrder_IsRejected_WithoutIncreasingErrors(int[]? payload)
    {
        var game = ReadyFinaleGame();
        var initialErrors = game.Progress.WrongAnswerCount;
        var initialVersion = game.Progress.Version;

        var accepted = game.TrySubmitFinale("a", payload, "cmd-malformed", out var res);

        Assert.False(accepted);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidAnswerPayload, res.ErrorCode);
        Assert.False(res.Correct);
        Assert.False(res.Mutated);
        Assert.Equal(initialErrors, game.Progress.WrongAnswerCount);
        Assert.Equal(initialVersion, game.Progress.Version);
        Assert.False(game.Progress.FinaleOrderCompleted);
    }

    // 3. Permutation sai tăng một lỗi và reset stage
    [Fact]
    public void IncorrectPermutation_IncreasesErrors_AndKeepsStage1()
    {
        var game = ReadyFinaleGame();
        var initialErrors = game.Progress.WrongAnswerCount;
        var initialVersion = game.Progress.Version;

        var accepted = game.TrySubmitFinale("a", [1, 0, 2, 3], "cmd-wrong-perm", out var res);

        Assert.True(accepted);
        Assert.False(res.Correct);
        Assert.True(res.Mutated);
        Assert.Equal(initialErrors + 1, game.Progress.WrongAnswerCount);
        Assert.Equal(initialVersion + 1, game.Progress.Version);
        Assert.False(game.Progress.FinaleOrderCompleted);
        Assert.Equal(0, game.Progress.ReturnStep);
        Assert.Equal(Chapter.Finale, game.Progress.Chapter);
        Assert.Null(game.Progress.FinishedAtUtc);
    }

    // 4. Replay permutation sai không tăng lần hai
    [Fact]
    public void Replay_IncorrectPermutation_DoesNotIncreaseErrorsTwice()
    {
        var game = ReadyFinaleGame();

        game.TrySubmitFinale("a", [3, 2, 1, 0], "cmd-replay-wrong", out var first);
        var errorsAfterFirst = game.Progress.WrongAnswerCount;
        var versionAfterFirst = game.Progress.Version;

        var replayed = game.TrySubmitFinale("a", [3, 2, 1, 0], "cmd-replay-wrong", out var second);

        Assert.True(replayed);
        Assert.False(second.Correct);
        Assert.False(second.Mutated);
        Assert.Equal(errorsAfterFirst, game.Progress.WrongAnswerCount);
        Assert.Equal(versionAfterFirst, game.Progress.Version);
    }

    // 5. Order đúng chỉ mở return stage, chưa Finished/timing Finale
    [Fact]
    public void CorrectOrder_UnlocksReturnStage_DoesNotFinishTeamOrRecordTiming()
    {
        var game = ReadyFinaleGame();
        var initialVersion = game.Progress.Version;
        var initialErrors = game.Progress.WrongAnswerCount;

        var accepted = game.TrySubmitFinale("a", [0, 1, 2, 3], "cmd-correct-order", out var res);

        Assert.True(accepted);
        Assert.True(res.Correct);
        Assert.True(res.Mutated);
        Assert.Equal(initialErrors, game.Progress.WrongAnswerCount);
        Assert.Equal(initialVersion + 1, game.Progress.Version);
        Assert.True(game.Progress.FinaleOrderCompleted);
        Assert.Equal(0, game.Progress.ReturnStep);
        Assert.Equal(Chapter.Finale, game.Progress.Chapter);
        Assert.False(game.Progress.FinaleDone);
        Assert.Null(game.Progress.FinishedAtUtc);
        Assert.DoesNotContain(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Finale);
    }

    // 6. Bỏ step hoặc lặp step bằng command mới bị INVALID_RETURN_STEP
    [Fact]
    public void InvalidReturnStep_SkipOrRepeatWithNewCommand_IsRejected()
    {
        var game = ReadyFinaleGame();
        game.TrySubmitFinale("a", [0, 1, 2, 3], "cmd-order", out _);
        var initialVersion = game.Progress.Version;
        var initialErrors = game.Progress.WrongAnswerCount;

        // Cố tình bỏ qua step 0, gửi step 1
        var skipAccepted = game.TrySubmitFinale("a", [1], "cmd-skip-step", out var skipRes);
        Assert.False(skipAccepted);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidReturnStep, skipRes.ErrorCode);
        Assert.False(skipRes.Mutated);
        Assert.Equal(initialErrors, game.Progress.WrongAnswerCount);
        Assert.Equal(initialVersion, game.Progress.Version);
        Assert.Equal(0, game.Progress.ReturnStep);

        // Hoàn thành step 0
        game.TrySubmitFinale("a", [0], "cmd-step-0", out _);
        Assert.Equal(1, game.Progress.ReturnStep);

        // Thử lặp lại step 0 bằng command mới
        var repeatAccepted = game.TrySubmitFinale("a", [0], "cmd-repeat-0", out var repeatRes);
        Assert.False(repeatAccepted);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidReturnStep, repeatRes.ErrorCode);
        Assert.False(repeatRes.Mutated);
        Assert.Equal(1, game.Progress.ReturnStep);
    }

    // 7. Replay step đúng không tăng ReturnStep lần hai
    [Fact]
    public void Replay_CorrectReturnStep_DoesNotAdvanceStepTwice()
    {
        var game = ReadyFinaleGame();
        game.TrySubmitFinale("a", [0, 1, 2, 3], "cmd-order", out _);

        game.TrySubmitFinale("a", [0], "cmd-step-0", out var first);
        Assert.Equal(1, game.Progress.ReturnStep);
        var versionAfterFirst = game.Progress.Version;

        var replayed = game.TrySubmitFinale("a", [0], "cmd-step-0", out var second);
        Assert.True(replayed);
        Assert.True(second.Correct);
        Assert.False(second.Mutated);
        Assert.Equal(1, game.Progress.ReturnStep);
        Assert.Equal(versionAfterFirst, game.Progress.Version);
    }

    // 8. Bốn step đúng tuần tự mới chuyển Complete
    [Fact]
    public void AllFourReturnSteps_InOrder_AdvancesToComplete()
    {
        var game = ReadyFinaleGame();
        game.TrySubmitFinale("a", [0, 1, 2, 3], "cmd-order", out _);

        // Step 0: Tiếp nhận
        game.TrySubmitFinale("a", [0], "cmd-step-0", out var r0);
        Assert.True(r0.Success);
        Assert.Equal(1, game.Progress.ReturnStep);
        Assert.Equal(Chapter.Finale, game.Progress.Chapter);

        // Step 1: Giải trình
        game.TrySubmitFinale("a", [1], "cmd-step-1", out var r1);
        Assert.True(r1.Success);
        Assert.Equal(2, game.Progress.ReturnStep);
        Assert.Equal(Chapter.Finale, game.Progress.Chapter);

        // Step 2: Điều chỉnh
        game.TrySubmitFinale("a", [2], "cmd-step-2", out var r2);
        Assert.True(r2.Success);
        Assert.Equal(3, game.Progress.ReturnStep);
        Assert.Equal(Chapter.Finale, game.Progress.Chapter);

        // Step 3: Trả kết quả
        game.TrySubmitFinale("a", [3], "cmd-step-3", out var r3);
        Assert.True(r3.Success);
        Assert.Equal(4, game.Progress.ReturnStep);
        Assert.True(game.Progress.FinaleDone);
        Assert.Equal(Chapter.Complete, game.Progress.Chapter);
        Assert.NotNull(game.Progress.FinishedAtUtc);
    }

    // 9. FinishedAt và timing Finale dùng thời gian server và chỉ ghi một lần
    [Fact]
    public void FinishedAtUtc_AndTiming_UsesServerTime_AndRecordedOnce()
    {
        var serverTime = new DateTimeOffset(2026, 9, 15, 12, 15, 0, TimeSpan.Zero);
        var game = ReadyFinaleGame();
        game.UtcNowProvider = () => serverTime;
        game.TrySubmitFinale("a", [0, 1, 2, 3], "cmd-order", out _);
        game.TrySubmitFinale("a", [0], "cmd-s0", out _);
        game.TrySubmitFinale("a", [1], "cmd-s1", out _);
        game.TrySubmitFinale("a", [2], "cmd-s2", out _);

        game.TrySubmitFinale("a", [3], "cmd-s3", out var res);

        Assert.Equal(serverTime, game.Progress.FinishedAtUtc);
        var finaleTiming = Assert.Single(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Finale);
        Assert.Equal(serverTime, finaleTiming.CompletedAtUtc);
        Assert.True(finaleTiming.ElapsedMilliseconds >= 0);
    }

    // 10. Hai final submit đồng thời chỉ một mutation
    [Fact]
    public void ConcurrentFinalSubmits_OnlyOneRecordsFinish()
    {
        var game = ReadyFinaleGame();
        game.TrySubmitFinale("a", [0, 1, 2, 3], "cmd-order", out _);
        game.TrySubmitFinale("a", [0], "cmd-s0", out _);
        game.TrySubmitFinale("a", [1], "cmd-s1", out _);
        game.TrySubmitFinale("a", [2], "cmd-s2", out _);

        int mutations = 0;
        var barrier = new Barrier(2);

        Parallel.Invoke(
            () =>
            {
                barrier.SignalAndWait();
                if (game.TrySubmitFinale("a", [3], "cmd-s3-thread1", out var r1) && r1.Mutated)
                    Interlocked.Increment(ref mutations);
            },
            () =>
            {
                barrier.SignalAndWait();
                if (game.TrySubmitFinale("a", [3], "cmd-s3-thread2", out var r2) && r2.Mutated)
                    Interlocked.Increment(ref mutations);
            }
        );

        Assert.Equal(1, mutations);
        Assert.Single(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Finale);
        Assert.NotNull(game.Progress.FinishedAtUtc);
    }

    // 11. Sai chapter hoặc thiếu order không được chạy return step
    [Fact]
    public void WrongChapter_OrMissingOrder_RejectsReturnStep()
    {
        var game = ReadyFinaleGame();

        // Đang ở Finale nhưng chưa order: gửi return step bị từ chối
        var noOrderAccepted = game.TrySubmitFinale("a", [0], "cmd-premature-return", out var noOrderRes);
        Assert.False(noOrderAccepted);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidAnswerPayload, noOrderRes.ErrorCode);

        // Đặt sang chapter khác: bị InvalidChapter
        game.Progress.Chapter = Chapter.News;
        var wrongChapterAccepted = game.TrySubmitFinale("a", [0], "cmd-wrong-chap", out var wrongChapRes);
        Assert.False(wrongChapterAccepted);
        Assert.Equal(InteractErrorCodes.InvalidChapter, wrongChapRes.ErrorCode);
    }

    // 12. Team đã Finished chặn movement/interact/reserve/submit mới
    [Fact]
    public void FinishedTeam_BlocksMovement_Interact_Reserve_AndSubmit()
    {
        var game = ReadyFinaleGame();
        CompleteGame(game);

        // Chặn movement
        var moved = game.TryProcessMovement("a", 1, 1, 1000, out var moveAck, out _);
        Assert.False(moved);
        Assert.Equal(PuzzleReservationErrorCodes.TeamAlreadyFinished, moveAck.ErrorCode);

        // Chặn interact
        var interacted = game.TryProcessInteract("a", "lore0", "cmd-interact-after-finish", out var interactRes);
        Assert.False(interacted);
        Assert.Equal(PuzzleReservationErrorCodes.TeamAlreadyFinished, interactRes.ErrorCode);

        // Chặn reserve
        var canReserve = game.CanReservePuzzle("a", PuzzleIds.Finale, out var reserveError);
        Assert.False(canReserve);
        Assert.Equal(PuzzleReservationErrorCodes.TeamAlreadyFinished, reserveError);

        // Chặn submit puzzle mới
        var submitted = game.TrySubmitFinale("a", [3], "cmd-new-submit-after-finish", out var submitRes);
        Assert.False(submitted);
        Assert.Equal(PuzzleReservationErrorCodes.TeamAlreadyFinished, submitRes.ErrorCode);
    }

    // 13. Replay command hoàn thành vẫn trả success với Mutated=false
    [Fact]
    public void Replay_FinishedCommand_ReturnsCachedSuccess_WithMutatedFalse()
    {
        var game = ReadyFinaleGame();
        game.TrySubmitFinale("a", [0, 1, 2, 3], "cmd-order", out _);
        game.TrySubmitFinale("a", [0], "cmd-s0", out _);
        game.TrySubmitFinale("a", [1], "cmd-s1", out _);
        game.TrySubmitFinale("a", [2], "cmd-s2", out _);
        game.TrySubmitFinale("a", [3], "cmd-final-step", out var original);
        Assert.True(original.Success);
        Assert.True(original.Mutated);

        var replayed = game.TrySubmitFinale("a", [3], "cmd-final-step", out var replay);
        Assert.True(replayed);
        Assert.True(replay.Success);
        Assert.True(replay.Correct);
        Assert.False(replay.Mutated);
    }

    // 14. Mất owner sau step 1, owner mới tiếp tục từ step 1, không quay lại 0 và không nhảy 2
    [Fact]
    public void OwnerChange_AfterStep1_NextOwnerContinuesFromCurrentStep()
    {
        var game = ReadyFinaleGame();
        game.AddMember("b", "Bình", "avatar2");

        // Player A hoàn thành order và step 0
        game.TrySubmitFinale("a", [0, 1, 2, 3], "cmd-order-a", out _);
        game.TrySubmitFinale("a", [0], "cmd-s0-a", out _);
        Assert.Equal(1, game.Progress.ReturnStep);

        // Player B tiếp quản: thử lặp lại step 0 -> lỗi
        var bRepeat0 = game.TrySubmitFinale("b", [0], "cmd-b-repeat0", out var bRepeatRes);
        Assert.False(bRepeat0);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidReturnStep, bRepeatRes.ErrorCode);

        // Player B thử nhảy sang step 2 -> lỗi
        var bSkipTo2 = game.TrySubmitFinale("b", [2], "cmd-b-skip2", out var bSkipRes);
        Assert.False(bSkipTo2);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidReturnStep, bSkipRes.ErrorCode);

        // Player B gửi đúng step 1 -> thành công
        var bStep1 = game.TrySubmitFinale("b", [1], "cmd-b-step1", out var bStep1Res);
        Assert.True(bStep1);
        Assert.Equal(2, game.Progress.ReturnStep);
    }

    // 15. Đội C không bị ảnh hưởng, room vẫn Playing
    [Fact]
    public void RedTeamFinishes_BlueTeamIsolated_RoomRemainsPlaying()
    {
        var room = new RoomInstance("room1", "DC-TEST", "Phòng thi", "token123", 600, true, false, false, false, false);
        room.TryAddPlayer("An", "conn-a", 10);
        room.TryAddPlayer("Bình", "conn-b", 10);
        room.TryAddPlayer("Chi", "conn-c", 10);

        room.TryAddTeam("Đội Đỏ", "#E53935", 2, 2, 2, 10);
        room.TryAddTeam("Đội Xanh", "#1E88E5", 2, 2, 2, 10);

        var teams = room.GetTeamSnapshots();
        var redTeamId = teams[0].TeamId;
        var blueTeamId = teams[1].TeamId;

        var a = room.GetPlayerSnapshots().First(p => p.DisplayName == "An");
        var b = room.GetPlayerSnapshots().First(p => p.DisplayName == "Bình");
        var c = room.GetPlayerSnapshots().First(p => p.DisplayName == "Chi");

        room.TryJoinTeam(a.PlayerId, redTeamId);
        room.TryJoinTeam(b.PlayerId, redTeamId);
        room.TryJoinTeam(c.PlayerId, blueTeamId);

        room.TrySetReady(a.PlayerId, true);
        room.TrySetReady(b.PlayerId, true);
        room.TrySetReady(c.PlayerId, true);

        var (started, _, matchId, _) = room.TryStartMatch("token123", 10);
        Assert.True(started);
        room.TryForceAdvanceToPlaying(matchId!);
        Assert.Equal(RoomStatus.Playing, room.Status);

        var redGame = room.GetTeamGame(redTeamId)!;
        var blueGame = room.GetTeamGame(blueTeamId)!;

        // Đưa Đội Đỏ về Finale và hoàn thành
        redGame.Progress.Chapter = Chapter.Finale;
        Array.Fill(redGame.Progress.Spoken, true);
        Array.Fill(redGame.Progress.Lamps, true);
        Array.Fill(redGame.Progress.Clues, true);
        redGame.Progress.DraftDone = true;
        redGame.Progress.RiverDone = true;
        redGame.Progress.NewsDone = true;

        CompleteGame(redGame, a.PlayerId);

        // Đánh dấu team finished trên room
        room.TryMarkTeamFinished(redTeamId, redGame.Progress.FinishedAtUtc!.Value, out var redSnap);

        Assert.NotNull(redSnap!.FinishedAtUtc);
        Assert.True(redGame.Progress.FinaleDone);
        Assert.Equal(Chapter.Complete, redGame.Progress.Chapter);

        // Đội Xanh hoàn toàn không bị ảnh hưởng, vẫn ở Opening với 0 mảnh
        Assert.Equal(Chapter.Opening, blueGame.Progress.Chapter);
        Assert.Equal(0, blueGame.Progress.Shards);
        Assert.Null(blueGame.Progress.FinishedAtUtc);
        Assert.False(blueGame.Progress.FinaleDone);

        // Trạng thái phòng vẫn là Playing (không kết thúc sớm)
        Assert.Equal(RoomStatus.Playing, room.Status);
    }

    // 16. Client không có field/API tự đặt Complete hoặc FinishedAt
    [Fact]
    public void ClientCannotForge_CompleteOrFinishedAt()
    {
        var session = new MultiplayerSession("p1", "Player", "quang");
        // Kiểm tra các property là read-only, chỉ nhận từ server qua snapshot
        Assert.False(session.FinaleDone);
        Assert.Null(session.FinishedAtUtc);
        Assert.Equal(0, session.ReturnStep);
        Assert.False(session.FinaleOrderCompleted);
    }

    private static TeamGameInstance ReadyFinaleGame()
    {
        var game = new TeamGameInstance("m1", "r1", "t1", "Đội Đỏ", "#E53935", StartedAt);
        game.AddMember("a", "An", "avatar1", 513f, 328f);
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
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, StartedAt.AddMinutes(1), 60_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, StartedAt.AddMinutes(2), 60_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.River, StartedAt.AddMinutes(3), 60_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.News, StartedAt.AddMinutes(4), 60_000));
        return game;
    }

    private static void CompleteGame(TeamGameInstance game, string playerId = "a")
    {
        game.TrySubmitFinale(playerId, [0, 1, 2, 3], "setup-order", out _);
        game.TrySubmitFinale(playerId, [0], "setup-step0", out _);
        game.TrySubmitFinale(playerId, [1], "setup-step1", out _);
        game.TrySubmitFinale(playerId, [2], "setup-step2", out _);
        game.TrySubmitFinale(playerId, [3], "setup-step3", out _);
    }
}

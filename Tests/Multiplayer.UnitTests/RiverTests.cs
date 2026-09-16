using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class RiverTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    // 1. Default [0, 0, 0, 0] là đáp án sai hợp lệ
    [Fact]
    public void DefaultSigns_IsValidWrongAnswer_IncrementsCountersAndVersion()
    {
        var game = ReadyGame();
        var beforeVersion = game.Progress.Version;

        var accepted = game.TrySubmitRiver("a", [0, 0, 0, 0], "wrong-default", out var response);

        Assert.True(accepted);
        Assert.True(response.Success);
        Assert.False(response.Correct);
        Assert.True(response.Mutated);
        Assert.Equal(1, response.WrongAnswerCount);
        Assert.Equal(1, game.Progress.WrongAnswerCount);
        Assert.Equal(1, game.Progress.RiverFailures);
        Assert.Equal(beforeVersion + 1, game.Progress.Version);
        Assert.Equal(Chapter.River, game.Progress.Chapter);
        Assert.Equal(2, game.Progress.Shards);
        Assert.DoesNotContain(game.Progress.ChapterTimings, t => t.Chapter == Chapter.River);
    }

    // 2. Replay cùng CommandId
    [Fact]
    public void ReplaySameCommandId_WhenFirstWasWrong_ReturnsOldWrongResultWithoutPenalty()
    {
        var game = ReadyGame();
        game.TrySubmitRiver("a", [0, 0, 0, 0], "cmd-wrong", out var first);
        var versionAfterFirst = game.Progress.Version;

        // Replay with correct answer using the same CommandId
        var replayed = game.TrySubmitRiver("a", [1, 2, 3, 0], "cmd-wrong", out var replay);

        Assert.True(first.Mutated);
        Assert.True(replayed);
        Assert.False(replay.Mutated);
        Assert.False(replay.Correct);
        Assert.Equal(1, game.Progress.WrongAnswerCount);
        Assert.Equal(1, game.Progress.RiverFailures);
        Assert.Equal(versionAfterFirst, game.Progress.Version);
        Assert.Equal(Chapter.River, game.Progress.Chapter);
    }

    // 3. Malformed theory: null, 3 elements, 5 elements, -1, 4
    [Theory]
    [MemberData(nameof(MalformedRiverPayloads))]
    public void MalformedRiverPayload_IsRejectedWithoutPenalty(int[]? answer)
    {
        var game = ReadyGame();
        var beforeVersion = game.Progress.Version;

        var accepted = game.TrySubmitRiver("a", answer, "malformed-cmd", out var response);

        Assert.False(accepted);
        Assert.False(response.Success);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidAnswerPayload, response.ErrorCode);
        Assert.Equal(0, game.Progress.WrongAnswerCount);
        Assert.Equal(0, game.Progress.RiverFailures);
        Assert.Equal(beforeVersion, game.Progress.Version);
        Assert.Equal(Chapter.River, game.Progress.Chapter);
    }

    public static TheoryData<int[]?> MalformedRiverPayloads => new()
    {
        null,
        new[] { 1, 2, 3 },             // 3 elements (missing)
        new[] { 1, 2, 3, 0, 1 },       // 5 elements (extra)
        new[] { -1, 2, 3, 0 },         // contains -1
        new[] { 1, 4, 3, 0 }           // contains 4
    };

    // 4. Đúng [1, 2, 3, 0]
    [Fact]
    public void CorrectAnswer_TransitionsToNews_ShardsBecomeThree_AndRecordsRiverTiming()
    {
        var completedAt = StartedAt.AddMinutes(3).AddSeconds(45);
        var game = ReadyGame();
        game.UtcNowProvider = () => completedAt;

        var accepted = game.TrySubmitRiver("a", [1, 2, 3, 0], "correct-cmd", out var response);

        Assert.True(accepted);
        Assert.True(response.Success);
        Assert.True(response.Correct);
        Assert.True(response.Mutated);
        Assert.True(game.Progress.RiverDone);
        Assert.Equal(Chapter.News, game.Progress.Chapter);
        Assert.Equal(3, game.Progress.Shards);

        // Giữ timing Lights và Draft có sẵn và thêm đúng một timing River
        Assert.Equal(3, game.Progress.ChapterTimings.Count);
        Assert.Contains(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Lights);
        Assert.Contains(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Draft);
        var riverTiming = Assert.Single(game.Progress.ChapterTimings, t => t.Chapter == Chapter.River);
        Assert.Equal(completedAt, riverTiming.CompletedAtUtc);
        Assert.Equal(225_000, riverTiming.ElapsedMilliseconds);
    }

    // 5. Replay đáp án đúng
    [Fact]
    public void ReplayCorrectAnswer_ReturnsSuccessWithoutMutationOrDuplicateTiming()
    {
        var game = ReadyGame();
        game.TrySubmitRiver("a", [1, 2, 3, 0], "correct-replay-cmd", out var first);
        var versionAfterCorrect = game.Progress.Version;
        var timingCount = game.Progress.ChapterTimings.Count;

        var replayed = game.TrySubmitRiver("a", [1, 2, 3, 0], "correct-replay-cmd", out var replay);

        Assert.True(first.Mutated);
        Assert.True(replayed);
        Assert.True(replay.Success);
        Assert.True(replay.Correct);
        Assert.False(replay.Mutated);
        Assert.Equal(versionAfterCorrect, game.Progress.Version);
        Assert.Equal(timingCount, game.Progress.ChapterTimings.Count);
        Assert.Equal(0, game.Progress.WrongAnswerCount);
    }

    // 6. Hai thread submit đồng thời cùng command đúng
    [Fact]
    public async Task ConcurrentSubmitSameCorrectCommand_MutatesOnlyOnce()
    {
        var game = ReadyGame();
        using var barrier = new Barrier(2);

        var task1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            game.TrySubmitRiver("a", [1, 2, 3, 0], "concurrent-correct", out var resp);
            return resp;
        });

        var task2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            game.TrySubmitRiver("a", [1, 2, 3, 0], "concurrent-correct", out var resp);
            return resp;
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.Single(results, r => r.Mutated);
        Assert.All(results, r => Assert.True(r.Correct));
        Assert.Equal(Chapter.News, game.Progress.Chapter);
        Assert.Single(game.Progress.ChapterTimings, t => t.Chapter == Chapter.River);
    }

    // 7. Đúng thứ tự khi chapter không phải River
    [Fact]
    public void CorrectAnswer_WhenNotInRiverChapter_IsRejectedWithInvalidChapter()
    {
        var game = ReadyGame();
        game.Progress.Chapter = Chapter.Draft;

        var accepted = game.TrySubmitRiver("a", [1, 2, 3, 0], "wrong-ch-cmd", out var response);

        Assert.False(accepted);
        Assert.False(response.Success);
        Assert.Equal(InteractErrorCodes.InvalidChapter, response.ErrorCode);
        Assert.DoesNotContain(game.Progress.ChapterTimings, t => t.Chapter == Chapter.River);
        Assert.Equal(1, game.Progress.Shards);
    }

    // 8. Player không thuộc team bị từ chối
    [Fact]
    public void PlayerNotInTeam_IsRejected()
    {
        var game = ReadyGame();

        var accepted = game.TrySubmitRiver("outsider", [1, 2, 3, 0], "outsider-cmd", out var response);

        Assert.False(accepted);
        Assert.False(response.Success);
        Assert.Equal(InteractErrorCodes.PlayerNotInTeam, response.ErrorCode);
    }

    // 9. Team isolation
    [Fact]
    public void TeamIsolation_RedTeamToNews_BlueTeamRemainsAtRiverWithOwnErrors()
    {
        var red = ReadyGame("red", "a");
        var blue = ReadyGame("blue", "c");

        red.TrySubmitRiver("a", [1, 2, 3, 0], "red-correct", out var redResp);
        blue.TrySubmitRiver("c", [0, 0, 0, 0], "blue-wrong", out var blueResp);

        Assert.True(redResp.Correct);
        Assert.Equal(Chapter.News, red.Progress.Chapter);
        Assert.Equal(3, red.Progress.Shards);
        Assert.Equal(0, red.Progress.WrongAnswerCount);

        Assert.False(blueResp.Correct);
        Assert.Equal(Chapter.River, blue.Progress.Chapter);
        Assert.Equal(2, blue.Progress.Shards);
        Assert.Equal(1, blue.Progress.WrongAnswerCount);
        Assert.Equal(1, blue.Progress.RiverFailures);
    }

    // 10. Namespace idempotency: cùng chuỗi CommandId cho Draft và River vẫn độc lập
    [Fact]
    public void NamespaceIdempotency_SameCommandId_TreatedIndependentlyForDraftAndRiver()
    {
        var game = ReadyGame();
        // Giả lập Draft đã dùng command ID này ở Chapter.Draft
        game.Progress.Chapter = Chapter.Draft;
        game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "shared-cmd-123", out var draftResp);
        Assert.True(draftResp.Success);

        // Submit Draft đúng đã chuyển sang Chapter.River, giờ dùng cùng CommandId "shared-cmd-123" cho River
        Assert.Equal(Chapter.River, game.Progress.Chapter);
        var riverAccepted = game.TrySubmitRiver("a", [1, 2, 3, 0], "shared-cmd-123", out var riverResp);

        Assert.True(riverAccepted);
        Assert.True(riverResp.Success);
        Assert.True(riverResp.Correct);
        Assert.True(riverResp.Mutated); // Không bị coi là replay của Draft!
        Assert.Equal(Chapter.News, game.Progress.Chapter);
    }

    // 11. Reorder local: TurnRiver đổi hướng biển, không làm đổi shared progress/version/chapter
    [Fact]
    public void TurnRiver_OnlyChangesLocalSign_DoesNotMutateSharedProgress()
    {
        var session = new MultiplayerSession("a", "An", "bao");
        var initialVersion = session.Progress.Version;
        var initialChapter = session.Progress.Chapter;
        var initialShards = session.Progress.Shards;
        var initialRiverDone = session.Progress.RiverDone;

        // Xoay biển trạm 0
        var signBefore = session.RiverSigns[0];
        session.TurnRiver(0);

        Assert.Equal((signBefore + 1) % 4, session.RiverSigns[0]);

        // Shared progress hoàn toàn không đổi
        Assert.Equal(initialVersion, session.Progress.Version);
        Assert.Equal(initialChapter, session.Progress.Chapter);
        Assert.Equal(initialShards, session.Progress.Shards);
        Assert.Equal(initialRiverDone, session.Progress.RiverDone);
    }

    // 12. Snapshot News khi client đang mở hội thoại: chapter/mảnh cập nhật, hội thoại không bị đè
    [Fact]
    public void SnapshotNews_WhileDialogueIsOpen_UpdatesStateWithoutClosingOrOverwritingDialogue()
    {
        var session = new MultiplayerSession("a", "An", "bao");
        session.ApplySnapshot(RiverSnapshot(1));
        session.ShowDialogue([
            new Line("Dũng", "Dòng ý kiến đã thông."),
            new Line("Nam", "Qua Bảng tin xem người dân phản ánh gì nhé.")
        ]);
        session.AdvanceDialogue(); // Đang ở câu thoại thứ 2 (index 1)

        Assert.Equal(1, session.DialogueIndex);
        Assert.Equal(Panel.Dialogue, session.Panel);

        // Nhận snapshot server chuyển sang News
        var newsSnapshot = NewsSnapshot(2);
        session.ApplySnapshot(newsSnapshot);

        // State được cập nhật lên News và 3 mảnh
        Assert.Equal(Chapter.News, session.Chapter);
        Assert.Equal(3, session.Shards);
        Assert.True(session.RiverDone);

        // Hội thoại hiện tại vẫn nguyên vẹn
        Assert.Equal(Panel.Dialogue, session.Panel);
        Assert.Equal(1, session.DialogueIndex);
        Assert.Equal("Qua Bảng tin xem người dân phản ánh gì nhé.", session.CurrentLine!.Text);
    }

    private static TeamGameInstance ReadyGame(string teamId = "red", string playerId = "a")
    {
        var game = new TeamGameInstance("match1", "room1", teamId, teamId, "#f00", StartedAt);
        game.AddMember(playerId, playerId, "bao", 823, 325);
        game.Progress.Chapter = Chapter.River;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        game.Progress.DraftDone = true;
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, StartedAt.AddMinutes(1), 60_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, StartedAt.AddMinutes(2), 60_000));
        return game;
    }

    private static TeamGameStateSnapshot RiverSnapshot(int version) => new(
        "match1", "red", "Đỏ", "#f00",
        [new TeamMemberState("a", "An", "bao", "#f00", 823, 325, 0, 0, true)],
        new TeamProgressSnapshot(Chapter.River, [true, true, true, true], [true, true, true, true],
            new bool[3], version, 2, 0,
            [
                new ChapterTiming(Chapter.Lights, StartedAt.AddMinutes(1), 60_000),
                new ChapterTiming(Chapter.Draft, StartedAt.AddMinutes(2), 60_000)
            ],
            DraftDone: true, DraftFailures: 0, RiverDone: false, RiverFailures: 0));

    private static TeamGameStateSnapshot NewsSnapshot(int version) => new(
        "match1", "red", "Đỏ", "#f00",
        [new TeamMemberState("a", "An", "bao", "#f00", 823, 325, 0, 0, true)],
        new TeamProgressSnapshot(Chapter.News, [true, true, true, true], [true, true, true, true],
            new bool[3], version, 3, 0,
            [
                new ChapterTiming(Chapter.Lights, StartedAt.AddMinutes(1), 60_000),
                new ChapterTiming(Chapter.Draft, StartedAt.AddMinutes(2), 60_000),
                new ChapterTiming(Chapter.River, StartedAt.AddMinutes(3), 60_000)
            ],
            DraftDone: true, DraftFailures: 0, RiverDone: true, RiverFailures: 0));
}

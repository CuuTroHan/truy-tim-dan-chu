using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class DraftTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    // 1. Default [2,0,5,1,4,3] là đáp án sai hợp lệ
    [Fact]
    public void DefaultSequence_IsValidWrongAnswer_IncrementsCountersAndVersion()
    {
        var game = ReadyGame();
        var beforeVersion = game.Progress.Version;

        var accepted = game.TrySubmitDraft("a", [2, 0, 5, 1, 4, 3], "wrong-default", out var response);

        Assert.True(accepted);
        Assert.True(response.Success);
        Assert.False(response.Correct);
        Assert.True(response.Mutated);
        Assert.Equal(1, response.WrongAnswerCount);
        Assert.Equal(1, game.Progress.WrongAnswerCount);
        Assert.Equal(1, game.Progress.DraftFailures);
        Assert.Equal(beforeVersion + 1, game.Progress.Version);
        Assert.Equal(Chapter.Draft, game.Progress.Chapter);
        Assert.Equal(1, game.Progress.Shards);
        Assert.DoesNotContain(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Draft);
    }

    // 2. Replay cùng CommandId
    [Fact]
    public void ReplaySameCommandId_WhenFirstWasWrong_ReturnsOldWrongResultWithoutPenalty()
    {
        var game = ReadyGame();
        game.TrySubmitDraft("a", [2, 0, 5, 1, 4, 3], "cmd-wrong", out var first);
        var versionAfterFirst = game.Progress.Version;

        // Replay with correct sequence using the same CommandId
        var replayed = game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "cmd-wrong", out var replay);

        Assert.True(first.Mutated);
        Assert.True(replayed);
        Assert.False(replay.Mutated);
        Assert.False(replay.Correct);
        Assert.Equal(1, game.Progress.WrongAnswerCount);
        Assert.Equal(1, game.Progress.DraftFailures);
        Assert.Equal(versionAfterFirst, game.Progress.Version);
        Assert.Equal(Chapter.Draft, game.Progress.Chapter);
    }

    // 3. Malformed theory: null, 5 elements, 7 elements, duplicate, -1, 6
    [Theory]
    [MemberData(nameof(MalformedDraftPayloads))]
    public void MalformedDraftPayload_IsRejectedWithoutPenalty(int[]? answer)
    {
        var game = ReadyGame();
        var beforeVersion = game.Progress.Version;

        var accepted = game.TrySubmitDraft("a", answer, "malformed-cmd", out var response);

        Assert.False(accepted);
        Assert.False(response.Success);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidAnswerPayload, response.ErrorCode);
        Assert.Equal(0, game.Progress.WrongAnswerCount);
        Assert.Equal(0, game.Progress.DraftFailures);
        Assert.Equal(beforeVersion, game.Progress.Version);
        Assert.Equal(Chapter.Draft, game.Progress.Chapter);
    }

    public static TheoryData<int[]?> MalformedDraftPayloads => new()
    {
        null,
        new[] { 0, 1, 2, 3, 4 },             // 5 elements (missing)
        new[] { 0, 1, 2, 3, 4, 5, 5 },       // 7 elements (extra)
        new[] { 0, 1, 2, 3, 5, 5 },          // duplicate 5 (length 6 but duplicate/missing 4)
        new[] { -1, 1, 2, 3, 4, 5 },         // contains -1
        new[] { 0, 1, 2, 3, 4, 6 }           // contains 6
    };

    // 4. Đúng [0..5]
    [Fact]
    public void CorrectAnswer_TransitionsToRiver_ShardsBecomeTwo_AndRecordsDraftTiming()
    {
        var completedAt = StartedAt.AddMinutes(2).AddSeconds(15).AddMilliseconds(500);
        var game = ReadyGame();
        game.UtcNowProvider = () => completedAt;

        var accepted = game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "correct-cmd", out var response);

        Assert.True(accepted);
        Assert.True(response.Success);
        Assert.True(response.Correct);
        Assert.True(response.Mutated);
        Assert.True(game.Progress.DraftDone);
        Assert.Equal(Chapter.River, game.Progress.Chapter);
        Assert.Equal(2, game.Progress.Shards);

        // Giữ timing Lights có sẵn và thêm đúng một timing Draft
        Assert.Equal(2, game.Progress.ChapterTimings.Count);
        Assert.Contains(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Lights);
        var draftTiming = Assert.Single(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Draft);
        Assert.Equal(completedAt, draftTiming.CompletedAtUtc);
        Assert.Equal(135_500, draftTiming.ElapsedMilliseconds);
    }

    // 5. Replay đáp án đúng
    [Fact]
    public void ReplayCorrectAnswer_ReturnsSuccessWithoutMutationOrDuplicateTiming()
    {
        var game = ReadyGame();
        game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "correct-replay-cmd", out var first);
        var versionAfterCorrect = game.Progress.Version;
        var timingCount = game.Progress.ChapterTimings.Count;

        var replayed = game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "correct-replay-cmd", out var replay);

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
            game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "concurrent-correct", out var resp);
            return resp;
        });

        var task2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "concurrent-correct", out var resp);
            return resp;
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.Single(results, r => r.Mutated);
        Assert.All(results, r => Assert.True(r.Correct));
        Assert.Equal(Chapter.River, game.Progress.Chapter);
        Assert.Single(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Draft);
    }

    // 7. Đúng thứ tự khi chapter không phải Draft
    [Fact]
    public void CorrectAnswer_WhenNotInDraftChapter_IsRejectedWithInvalidChapter()
    {
        var game = ReadyGame();
        game.Progress.Chapter = Chapter.Lights;

        var accepted = game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "wrong-ch-cmd", out var response);

        Assert.False(accepted);
        Assert.False(response.Success);
        Assert.Equal(InteractErrorCodes.InvalidChapter, response.ErrorCode);
        Assert.DoesNotContain(game.Progress.ChapterTimings, t => t.Chapter == Chapter.Draft);
        Assert.Equal(0, game.Progress.Shards);
    }

    // 8. Player không thuộc team bị từ chối
    [Fact]
    public void PlayerNotInTeam_IsRejected()
    {
        var game = ReadyGame();

        var accepted = game.TrySubmitDraft("outsider", [0, 1, 2, 3, 4, 5], "outsider-cmd", out var response);

        Assert.False(accepted);
        Assert.False(response.Success);
        Assert.Equal(InteractErrorCodes.PlayerNotInTeam, response.ErrorCode);
    }

    // 9. Team isolation
    [Fact]
    public void TeamIsolation_RedTeamToRiver_BlueTeamRemainsAtDraftWithOwnErrors()
    {
        var red = ReadyGame("red", "a");
        var blue = ReadyGame("blue", "c");

        red.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "red-correct", out var redResp);
        blue.TrySubmitDraft("c", [2, 0, 5, 1, 4, 3], "blue-wrong", out var blueResp);

        Assert.True(redResp.Correct);
        Assert.Equal(Chapter.River, red.Progress.Chapter);
        Assert.Equal(2, red.Progress.Shards);
        Assert.Equal(0, red.Progress.WrongAnswerCount);

        Assert.False(blueResp.Correct);
        Assert.Equal(Chapter.Draft, blue.Progress.Chapter);
        Assert.Equal(1, blue.Progress.Shards);
        Assert.Equal(1, blue.Progress.WrongAnswerCount);
        Assert.Equal(1, blue.Progress.DraftFailures);
    }

    // 10. Namespace idempotency: cùng chuỗi CommandId cho Mirrors và Draft vẫn độc lập
    [Fact]
    public void NamespaceIdempotency_SameCommandId_TreatedIndependentlyForMirrorsAndDraft()
    {
        var game = ReadyGame();
        // Giả lập Mirrors đã dùng command ID này ở Chapter.Lights
        game.Progress.Chapter = Chapter.Lights;
        game.TrySubmitMirrors("a", [1, 2, 3, 0], "shared-cmd-123", out var mirrorResp);
        Assert.True(mirrorResp.Success);

        // Submit Mirrors đúng đã chuyển sang Chapter.Draft, giờ dùng cùng CommandId "shared-cmd-123" cho Draft
        Assert.Equal(Chapter.Draft, game.Progress.Chapter);
        var draftAccepted = game.TrySubmitDraft("a", [0, 1, 2, 3, 4, 5], "shared-cmd-123", out var draftResp);

        Assert.True(draftAccepted);
        Assert.True(draftResp.Success);
        Assert.True(draftResp.Correct);
        Assert.True(draftResp.Mutated); // Không bị coi là replay của Mirrors!
        Assert.Equal(Chapter.River, game.Progress.Chapter);
    }

    // 11. Reorder local: MoveDraft đổi thứ tự hiển thị, không làm đổi shared progress/version/chapter
    [Fact]
    public void MoveDraft_OnlyChangesLocalSequence_DoesNotMutateSharedProgress()
    {
        var session = new MultiplayerSession("a", "An", "bao");
        var initialVersion = session.Progress.Version;
        var initialChapter = session.Progress.Chapter;
        var initialShards = session.Progress.Shards;
        var initialDraftDone = session.Progress.DraftDone;

        // Hoán đổi bước 0 và bước 1
        var firstBefore = session.DraftLabels[0];
        var secondBefore = session.DraftLabels[1];
        session.MoveDraft(0, 1);

        Assert.Equal(firstBefore, session.DraftLabels[1]);
        Assert.Equal(secondBefore, session.DraftLabels[0]);

        // Shared progress hoàn toàn không đổi
        Assert.Equal(initialVersion, session.Progress.Version);
        Assert.Equal(initialChapter, session.Progress.Chapter);
        Assert.Equal(initialShards, session.Progress.Shards);
        Assert.Equal(initialDraftDone, session.Progress.DraftDone);
    }

    // 12. Snapshot River khi client đang mở hội thoại: chapter/mảnh cập nhật, hội thoại không bị đè
    [Fact]
    public void SnapshotRiver_WhileDialogueIsOpen_UpdatesStateWithoutClosingOrOverwritingDialogue()
    {
        var session = new MultiplayerSession("a", "An", "bao");
        session.ApplySnapshot(DraftSnapshot(1));
        session.ShowDialogue([
            new Line("Dân làng", "Đang đọc tâm tư của người dân."),
            new Line("Dân làng", "Bước thứ hai trong cuộc trao đổi.")
        ]);
        session.AdvanceDialogue(); // Đang ở câu thoại thứ 2 (index 1)

        Assert.Equal(1, session.DialogueIndex);
        Assert.Equal(Panel.Dialogue, session.Panel);

        // Nhận snapshot server chuyển sang River
        var riverSnapshot = RiverSnapshot(2);
        session.ApplySnapshot(riverSnapshot);

        // State được cập nhật lên River và 2 mảnh
        Assert.Equal(Chapter.River, session.Chapter);
        Assert.Equal(2, session.Shards);
        Assert.True(session.DraftDone);

        // Hội thoại hiện tại vẫn nguyên vẹn
        Assert.Equal(Panel.Dialogue, session.Panel);
        Assert.Equal(1, session.DialogueIndex);
        Assert.Equal("Bước thứ hai trong cuộc trao đổi.", session.CurrentLine!.Text);
    }

    private static TeamGameInstance ReadyGame(string teamId = "red", string playerId = "a")
    {
        var game = new TeamGameInstance("match1", "room1", teamId, teamId, "#f00", StartedAt);
        game.AddMember(playerId, playerId, "bao", 486, 115);
        game.Progress.Chapter = Chapter.Draft;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, StartedAt.AddMinutes(1), 60_000));
        return game;
    }

    private static TeamGameStateSnapshot DraftSnapshot(int version) => new(
        "match1", "red", "Đỏ", "#f00",
        [new TeamMemberState("a", "An", "bao", "#f00", 486, 115, 0, 0, true)],
        new TeamProgressSnapshot(Chapter.Draft, [true, true, true, true], [true, true, true, true],
            new bool[3], version, 1, 0,
            [new ChapterTiming(Chapter.Lights, StartedAt.AddMinutes(1), 60_000)],
            DraftDone: false, DraftFailures: 0));

    private static TeamGameStateSnapshot RiverSnapshot(int version) => new(
        "match1", "red", "Đỏ", "#f00",
        [new TeamMemberState("a", "An", "bao", "#f00", 486, 115, 0, 0, true)],
        new TeamProgressSnapshot(Chapter.River, [true, true, true, true], [true, true, true, true],
            new bool[3], version, 2, 0,
            [
                new ChapterTiming(Chapter.Lights, StartedAt.AddMinutes(1), 60_000),
                new ChapterTiming(Chapter.Draft, StartedAt.AddMinutes(2), 120_000)
            ],
            DraftDone: true, DraftFailures: 0));
}

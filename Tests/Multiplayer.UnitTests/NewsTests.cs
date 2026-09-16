using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class NewsTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    // 1. Correct choice thiếu từng clue bị prerequisite
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Choice2_MissingAnyClue_IsRejectedWithPrerequisiteNotMet(bool c0, bool c1, bool c2)
    {
        var game = ReadyNewsGame();
        game.Progress.Clues[0] = c0;
        game.Progress.Clues[1] = c1;
        game.Progress.Clues[2] = c2;
        var initialVersion = game.Progress.Version;
        var initialErrors = game.Progress.WrongAnswerCount;

        var accepted = game.TrySubmitNews("a", [2], "cmd-premature", out var res);

        Assert.False(accepted);
        Assert.Equal(PuzzleReservationErrorCodes.PrerequisiteNotMet, res.ErrorCode);
        Assert.False(res.Correct);
        Assert.False(res.Mutated);
        Assert.Equal(initialVersion, game.Progress.Version);
        Assert.Equal(initialErrors, game.Progress.WrongAnswerCount);
        Assert.Equal(Chapter.News, game.Progress.Chapter);
        Assert.False(game.Progress.NewsDone);
    }

    // 2. Choice 0 tăng lỗi và NewsNoise
    [Fact]
    public void Choice0_IncreasesWrongAnswerCount_AndNewsNoise()
    {
        var game = ReadyNewsGame();
        var initialVersion = game.Progress.Version;
        var initialErrors = game.Progress.WrongAnswerCount;
        var initialNoise = game.Progress.NewsNoise;

        var accepted = game.TrySubmitNews("a", [0], "cmd-choice-0", out var res);

        Assert.True(accepted);
        Assert.False(res.Correct);
        Assert.True(res.Mutated);
        Assert.Equal(initialErrors + 1, game.Progress.WrongAnswerCount);
        Assert.Equal(initialNoise + 1, game.Progress.NewsNoise);
        Assert.Equal(initialVersion + 1, game.Progress.Version);
        Assert.Equal(Chapter.News, game.Progress.Chapter);
        Assert.False(game.Progress.NewsDone);
    }

    // 3. Choice 1 tăng lỗi nhưng không tăng NewsNoise
    [Fact]
    public void Choice1_IncreasesWrongAnswerCount_WithoutIncreasingNewsNoise()
    {
        var game = ReadyNewsGame();
        var initialVersion = game.Progress.Version;
        var initialErrors = game.Progress.WrongAnswerCount;
        var initialNoise = game.Progress.NewsNoise;

        var accepted = game.TrySubmitNews("a", [1], "cmd-choice-1", out var res);

        Assert.True(accepted);
        Assert.False(res.Correct);
        Assert.True(res.Mutated);
        Assert.Equal(initialErrors + 1, game.Progress.WrongAnswerCount);
        Assert.Equal(initialNoise, game.Progress.NewsNoise);
        Assert.Equal(initialVersion + 1, game.Progress.Version);
        Assert.Equal(Chapter.News, game.Progress.Chapter);
        Assert.False(game.Progress.NewsDone);
    }

    // 4. Mỗi lựa chọn sai không mở Finale
    [Fact]
    public void WrongChoices_DoNotAdvanceToFinale()
    {
        var game = ReadyNewsGame();
        game.Progress.Clues[0] = true;
        game.Progress.Clues[1] = true;
        game.Progress.Clues[2] = true;

        game.TrySubmitNews("a", [0], "cmd-wrong-0", out _);
        Assert.Equal(Chapter.News, game.Progress.Chapter);
        Assert.Equal(3, game.Progress.Shards);
        Assert.False(game.Progress.NewsDone);

        game.TrySubmitNews("a", [1], "cmd-wrong-1", out _);
        Assert.Equal(Chapter.News, game.Progress.Chapter);
        Assert.Equal(3, game.Progress.Shards);
        Assert.False(game.Progress.NewsDone);
    }

    // 5. Malformed không tăng lỗi/version
    [Theory]
    [InlineData(null)]
    [InlineData(new int[0])]
    [InlineData(new int[] { 0, 1 })]
    [InlineData(new int[] { -1 })]
    [InlineData(new int[] { 3 })]
    public void MalformedPayload_IsRejectedWithoutMutating(int[]? payload)
    {
        var game = ReadyNewsGame();
        var initialVersion = game.Progress.Version;
        var initialErrors = game.Progress.WrongAnswerCount;

        var accepted = game.TrySubmitNews("a", payload, "cmd-malformed", out var res);

        Assert.False(accepted);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidAnswerPayload, res.ErrorCode);
        Assert.False(res.Mutated);
        Assert.Equal(initialVersion, game.Progress.Version);
        Assert.Equal(initialErrors, game.Progress.WrongAnswerCount);
    }

    // 6. Đủ clue + choice 2 chuyển Finale, 4 mảnh, timing News
    [Fact]
    public void Choice2_WithAllClues_AdvancesToFinale_FourShards_AndRecordsTiming()
    {
        var game = ReadyNewsGame();
        game.Progress.Clues[0] = true;
        game.Progress.Clues[1] = true;
        game.Progress.Clues[2] = true;
        game.Progress.NewsNoise = 2;
        var initialVersion = game.Progress.Version;

        var accepted = game.TrySubmitNews("a", [2], "cmd-correct", out var res);

        Assert.True(accepted);
        Assert.True(res.Correct);
        Assert.True(res.Mutated);
        Assert.True(game.Progress.NewsDone);
        Assert.Equal(0, game.Progress.NewsNoise);
        Assert.Equal(Chapter.Finale, game.Progress.Chapter);
        Assert.Equal(4, game.Progress.Shards);
        Assert.Equal(initialVersion + 1, game.Progress.Version);

        // Exactly 4 timings: Lights, Draft, River, News
        Assert.Equal(4, game.Progress.ChapterTimings.Count);
        Assert.Contains(game.Progress.ChapterTimings, t => t.Chapter == Chapter.News);
    }

    // 7. FinishedAtUtc vẫn null
    [Fact]
    public void CompletingNews_KeepsFinishedAtUtcNull()
    {
        var game = ReadyNewsGame();
        game.Progress.Clues[0] = true;
        game.Progress.Clues[1] = true;
        game.Progress.Clues[2] = true;

        game.TrySubmitNews("a", [2], "cmd-correct-finish-check", out _);

        Assert.Null(game.Progress.FinishedAtUtc);
        Assert.False(game.Progress.FinaleDone);
    }

    // 8. Replay sai và đúng không mutation lần hai
    [Fact]
    public void ReplayWrongAndCorrectCommands_ReturnsCachedResultWithoutDuplicateMutation()
    {
        var game = ReadyNewsGame();
        game.Progress.Clues[0] = true;
        game.Progress.Clues[1] = true;
        game.Progress.Clues[2] = true;

        // Replay wrong
        game.TrySubmitNews("a", [0], "cmd-replay-wrong", out var wrong1);
        var errorsAfterWrong = game.Progress.WrongAnswerCount;
        var versionAfterWrong = game.Progress.Version;
        game.TrySubmitNews("a", [0], "cmd-replay-wrong", out var wrong2);

        Assert.True(wrong1.Mutated);
        Assert.False(wrong2.Mutated);
        Assert.Equal(errorsAfterWrong, game.Progress.WrongAnswerCount);
        Assert.Equal(versionAfterWrong, game.Progress.Version);

        // Replay correct
        game.TrySubmitNews("a", [2], "cmd-replay-correct", out var cor1);
        var timingCount = game.Progress.ChapterTimings.Count;
        var versionAfterCorrect = game.Progress.Version;
        game.TrySubmitNews("a", [2], "cmd-replay-correct", out var cor2);

        Assert.True(cor1.Mutated);
        Assert.False(cor2.Mutated);
        Assert.Equal(timingCount, game.Progress.ChapterTimings.Count);
        Assert.Equal(versionAfterCorrect, game.Progress.Version);
    }

    // 9. Submit đồng thời đúng chỉ tạo một timing
    [Fact]
    public void ConcurrentCorrectSubmissions_OnlyAddOneTiming()
    {
        var game = ReadyNewsGame();
        game.Progress.Clues[0] = true;
        game.Progress.Clues[1] = true;
        game.Progress.Clues[2] = true;

        var ok1 = game.TrySubmitNews("a", [2], "cmd-concurrent-1", out var res1);
        var ok2 = game.TrySubmitNews("b", [2], "cmd-concurrent-2", out var res2);

        Assert.True(ok1);
        Assert.True(res1.Mutated);
        // Second command sees NewsDone/Finale already -> InvalidChapter
        Assert.False(ok2);
        Assert.Equal(InteractErrorCodes.InvalidChapter, res2.ErrorCode);
        Assert.Equal(1, game.Progress.ChapterTimings.Count(t => t.Chapter == Chapter.News));
    }

    // 10. Sai chapter/player ngoài đội/team isolation
    [Theory]
    [InlineData(Chapter.Opening)]
    [InlineData(Chapter.Lights)]
    [InlineData(Chapter.Draft)]
    [InlineData(Chapter.River)]
    public void SubmitNewsOutsideNewsChapter_IsRejectedWithInvalidChapter(Chapter chapter)
    {
        var game = new TeamGameInstance("match1", "room1", "teamRed", "Đội Đỏ", "#E53935", StartedAt);
        game.AddMember("a", "An", "bao", 246, 516);
        game.Progress.Chapter = chapter;

        var accepted = game.TrySubmitNews("a", [2], "cmd-wrong-ch", out var res);

        Assert.False(accepted);
        Assert.Equal(InteractErrorCodes.InvalidChapter, res.ErrorCode);
    }

    [Fact]
    public void PlayerNotInTeam_IsRejected()
    {
        var game = ReadyNewsGame();
        var accepted = game.TrySubmitNews("intruder", [2], "cmd-intruder", out var res);

        Assert.False(accepted);
        Assert.Equal(InteractErrorCodes.PlayerNotInTeam, res.ErrorCode);
    }

    [Fact]
    public void OtherTeam_IsCompletelyIsolated()
    {
        var teamA = ReadyNewsGame("teamA");
        var teamB = ReadyNewsGame("teamB");
        teamA.Progress.Clues[0] = true;
        teamA.Progress.Clues[1] = true;
        teamA.Progress.Clues[2] = true;

        teamA.TrySubmitNews("a", [2], "cmd-teamA", out _);

        Assert.True(teamA.Progress.NewsDone);
        Assert.Equal(Chapter.Finale, teamA.Progress.Chapter);
        Assert.Equal(4, teamA.Progress.Shards);

        Assert.False(teamB.Progress.NewsDone);
        Assert.Equal(Chapter.News, teamB.Progress.Chapter);
        Assert.Equal(3, teamB.Progress.Shards);
    }

    private static TeamGameInstance ReadyNewsGame(string teamId = "teamRed")
    {
        var game = new TeamGameInstance("match1", "room1", teamId, "Đội Đỏ", "#E53935", StartedAt);
        game.AddMember("a", "An", "bao", 246, 516);
        game.AddMember("b", "Bình", "bao", 246, 516);

        game.Progress.Chapter = Chapter.News;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        game.Progress.DraftDone = true;
        game.Progress.RiverDone = true;
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, StartedAt.AddMinutes(1), 60_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, StartedAt.AddMinutes(2), 120_000));
        game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.River, StartedAt.AddMinutes(3), 180_000));
        return game;
    }
}

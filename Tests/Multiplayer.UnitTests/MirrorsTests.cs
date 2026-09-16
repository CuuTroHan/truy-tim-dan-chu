using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class MirrorsTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WrongValidAnswer_IncrementsWrongCountAndVersion()
    {
        var game = ReadyGame();
        var before = game.Progress.Version;

        var accepted = game.TrySubmitMirrors("a", [0, 0, 0, 0], "wrong-1", out var response);

        Assert.True(accepted);
        Assert.True(response.Success);
        Assert.False(response.Correct);
        Assert.True(response.Mutated);
        Assert.Equal(1, response.WrongAnswerCount);
        Assert.Equal(before + 1, game.Progress.Version);
        Assert.Equal(Chapter.Lights, game.Progress.Chapter);
    }

    [Fact]
    public void ReplayWrongCommand_DoesNotCountTwice()
    {
        var game = ReadyGame();
        game.TrySubmitMirrors("a", [0, 0, 0, 0], "same-command", out var first);
        game.TrySubmitMirrors("a", [1, 2, 3, 0], "same-command", out var replay);

        Assert.True(first.Mutated);
        Assert.False(replay.Mutated);
        Assert.False(replay.Correct);
        Assert.Equal(1, game.Progress.WrongAnswerCount);
        Assert.Equal(Chapter.Lights, game.Progress.Chapter);
    }

    [Theory]
    [MemberData(nameof(MalformedAnswers))]
    public void MalformedAnswer_IsRejectedWithoutPenalty(int[]? answer)
    {
        var game = ReadyGame();
        var before = game.Progress.Version;

        game.TrySubmitMirrors("a", answer, "malformed", out var response);

        Assert.False(response.Success);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidAnswerPayload, response.ErrorCode);
        Assert.Equal(0, game.Progress.WrongAnswerCount);
        Assert.Equal(before, game.Progress.Version);
    }

    public static TheoryData<int[]?> MalformedAnswers => new()
    {
        null,
        new[] { 1, 2, 3 },
        new[] { 1, 2, 3, 0, 0 },
        new[] { 1, 2, 4, 0 },
        new[] { -1, 2, 3, 0 }
    };

    [Fact]
    public void CorrectAnswer_AdvancesOnceAndRecordsFirstChapterTiming()
    {
        var completedAt = StartedAt.AddSeconds(42).AddMilliseconds(375);
        var game = ReadyGame();
        game.UtcNowProvider = () => completedAt;

        game.TrySubmitMirrors("a", [1, 2, 3, 0], "correct", out var response);

        Assert.True(response.Success);
        Assert.True(response.Correct);
        Assert.True(response.Mutated);
        Assert.Equal(Chapter.Draft, game.Progress.Chapter);
        Assert.Equal(1, game.Progress.Shards);
        var timing = Assert.Single(game.Progress.ChapterTimings);
        Assert.Equal(Chapter.Lights, timing.Chapter);
        Assert.Equal(completedAt, timing.CompletedAtUtc);
        Assert.Equal(42_375, timing.ElapsedMilliseconds);

        Assert.True(game.TryGetPuzzleCommandReplay("a", PuzzleIds.Mirrors, "correct", out var replay));
        Assert.True(replay.Correct);
        Assert.False(replay.Mutated);
        Assert.Single(game.Progress.ChapterTimings);
    }

    [Fact]
    public async Task ConcurrentReplayOfCorrectCommand_MutatesOnlyOnce()
    {
        var game = ReadyGame();
        using var barrier = new Barrier(2);
        var first = Task.Run(() =>
        {
            barrier.SignalAndWait();
            game.TrySubmitMirrors("a", [1, 2, 3, 0], "same-correct", out var response);
            return response;
        });
        var second = Task.Run(() =>
        {
            barrier.SignalAndWait();
            game.TrySubmitMirrors("a", [1, 2, 3, 0], "same-correct", out var response);
            return response;
        });

        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, x => x.Mutated);
        Assert.All(responses, x => Assert.True(x.Correct));
        Assert.Equal(Chapter.Draft, game.Progress.Chapter);
        Assert.Single(game.Progress.ChapterTimings);
    }

    [Fact]
    public void CorrectAnswer_RequiresAllNpcAndLamps()
    {
        var game = ReadyGame();
        game.Progress.Spoken[2] = false;
        game.Progress.Lamps[3] = false;

        game.TrySubmitMirrors("a", [1, 2, 3, 0], "premature", out var response);

        Assert.False(response.Success);
        Assert.Equal(PuzzleReservationErrorCodes.PrerequisiteNotMet, response.ErrorCode);
        Assert.Equal(Chapter.Lights, game.Progress.Chapter);
        Assert.Empty(game.Progress.ChapterTimings);
    }

    [Fact]
    public void CorrectAnswer_InWrongChapter_IsRejected()
    {
        var game = ReadyGame();
        game.Progress.Chapter = Chapter.Draft;

        game.TrySubmitMirrors("a", [1, 2, 3, 0], "late", out var response);

        Assert.False(response.Success);
        Assert.Equal(InteractErrorCodes.InvalidChapter, response.ErrorCode);
        Assert.Empty(game.Progress.ChapterTimings);
    }

    [Fact]
    public void TeamsKeepIndependentProgressAndWrongCounts()
    {
        var red = ReadyGame("red", "a");
        var blue = ReadyGame("blue", "c");

        red.TrySubmitMirrors("a", [1, 2, 3, 0], "red-correct", out _);
        blue.TrySubmitMirrors("c", [0, 0, 0, 0], "blue-wrong", out _);

        Assert.Equal(Chapter.Draft, red.Progress.Chapter);
        Assert.Equal(0, red.Progress.WrongAnswerCount);
        Assert.Equal(Chapter.Lights, blue.Progress.Chapter);
        Assert.Equal(1, blue.Progress.WrongAnswerCount);
    }

    [Fact]
    public void ServerChapterUpdate_DoesNotBlockOnOrOverwriteLocalDialogue()
    {
        var session = new MultiplayerSession("a", "An", "bao");
        session.ApplySnapshot(Snapshot(Chapter.Lights, 1));
        session.ShowDialogue([new Line("Phương", "Đối thoại đang được đọc riêng.")]);

        session.ApplySnapshot(Snapshot(Chapter.Draft, 2));

        Assert.Equal(Chapter.Draft, session.Chapter);
        Assert.Equal(Panel.Dialogue, session.Panel);
        Assert.Equal("Đối thoại đang được đọc riêng.", session.CurrentLine!.Text);
    }

    private static TeamGameInstance ReadyGame(string teamId = "red", string playerId = "a")
    {
        var game = new TeamGameInstance("match1", "room1", teamId, teamId, "#f00", StartedAt);
        game.AddMember(playerId, playerId, "bao", 195, 323);
        game.Progress.Chapter = Chapter.Lights;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        return game;
    }

    private static TeamGameStateSnapshot Snapshot(Chapter chapter, int version) => new(
        "match1", "red", "Đỏ", "#f00",
        [new TeamMemberState("a", "An", "bao", "#f00", 195, 323, 0, 0, true)],
        new TeamProgressSnapshot(chapter, [true, true, true, true], [true, true, true, true],
            new bool[3], version, chapter == Chapter.Draft ? 1 : 0));
}

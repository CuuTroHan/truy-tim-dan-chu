using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class ClueTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    // 1. Ba command đồng thời lấy ba clue và giữ đủ ba cờ
    [Fact]
    public void ThreeConcurrentCommands_CollectThreeClues_AllFlagsPreserved()
    {
        var game = ReadyNewsGame();
        var initialVersion = game.Progress.Version;

        // Simulate 3 commands from team members
        var ok0 = game.TryProcessInteract("a", "clue0", "cmd-clue-0", out var res0);
        var ok1 = game.TryProcessInteract("b", "clue1", "cmd-clue-1", out var res1);
        var ok2 = game.TryProcessInteract("c", "clue2", "cmd-clue-2", out var res2);

        Assert.True(ok0);
        Assert.True(ok1);
        Assert.True(ok2);
        Assert.True(res0.Mutated);
        Assert.True(res1.Mutated);
        Assert.True(res2.Mutated);

        Assert.True(game.Progress.Clues[0]);
        Assert.True(game.Progress.Clues[1]);
        Assert.True(game.Progress.Clues[2]);
        Assert.Equal(initialVersion + 3, game.Progress.Version);
        Assert.Equal(Chapter.News, game.Progress.Chapter);
        Assert.Equal(3, game.Progress.Shards);
    }

    // 2. Hai người cùng lấy một clue chỉ mutation một lần
    [Fact]
    public void TwoPlayersCollectSameClue_OnlyOneMutates()
    {
        var game = ReadyNewsGame();
        var initialVersion = game.Progress.Version;
        MoveMember(game, "b", 180, 520);

        var ok1 = game.TryProcessInteract("a", "clue0", "cmd-a-clue0", out var res1);
        var ok2 = game.TryProcessInteract("b", "clue0", "cmd-b-clue0", out var res2);

        Assert.True(ok1);
        Assert.True(ok2);
        Assert.True(res1.Mutated);
        Assert.False(res2.Mutated);
        Assert.Equal(initialVersion + 1, game.Progress.Version);
        Assert.True(game.Progress.Clues[0]);
    }

    // 3. clue2 và Bảo cùng cờ, không cộng hai lần
    [Fact]
    public void Clue2AndBao_ShareSameFlag_DoNotDoubleCount()
    {
        var game = ReadyNewsGame();
        var initialVersion = game.Progress.Version;
        MoveMember(game, "a", 259, 537);
        MoveMember(game, "b", 286, 552);

        // Player A talks to Bao -> sets Clues[2]
        var okBao = game.TryProcessInteract("a", "bao", "cmd-bao", out var resBao);
        Assert.True(okBao);
        Assert.True(resBao.Mutated);
        Assert.True(game.Progress.Clues[2]);
        Assert.Equal(initialVersion + 1, game.Progress.Version);

        // Player B interacts with clue2 -> already true, no mutation
        var okClue2 = game.TryProcessInteract("b", "clue2", "cmd-clue2", out var resClue2);
        Assert.True(okClue2);
        Assert.False(resClue2.Mutated);
        Assert.True(game.Progress.Clues[2]);
        Assert.Equal(initialVersion + 1, game.Progress.Version);
    }

    // 4. Clue mới không làm mất cờ đã có
    [Fact]
    public void NewClue_DoesNotOverwriteExistingClues()
    {
        var game = ReadyNewsGame();
        game.TryProcessInteract("a", "clue0", "c0", out _);
        Assert.True(game.Progress.Clues[0]);
        Assert.False(game.Progress.Clues[1]);

        game.TryProcessInteract("b", "clue1", "c1", out _);
        Assert.True(game.Progress.Clues[0]);
        Assert.True(game.Progress.Clues[1]);
        Assert.False(game.Progress.Clues[2]);
    }

    // 5. Clue ngoài News bị từ chối với INVALID_CHAPTER
    [Theory]
    [InlineData(Chapter.Opening)]
    [InlineData(Chapter.Lights)]
    [InlineData(Chapter.Draft)]
    [InlineData(Chapter.River)]
    public void CluesOutsideNewsChapter_AreRejectedWithInvalidChapter(Chapter chapter)
    {
        var game = ReadyGameAtChapter(chapter);
        var initialVersion = game.Progress.Version;

        var accepted = game.TryProcessInteract("a", "clue0", "cmd-wrong-ch", out var res);

        Assert.False(accepted);
        Assert.Equal(InteractErrorCodes.InvalidChapter, res.ErrorCode);
        Assert.False(res.Mutated);
        Assert.False(game.Progress.Clues[0]);
        Assert.Equal(initialVersion, game.Progress.Version);
    }

    // 6. Lore lần đầu mutation, lần sau idempotent
    [Fact]
    public void Lore_FirstTimeMutates_SecondTimeIdempotent()
    {
        var game = ReadyNewsGame();
        var initialVersion = game.Progress.Version;
        MoveMember(game, "a", 344, 80);
        MoveMember(game, "b", 344, 80);

        var ok1 = game.TryProcessInteract("a", "lore0", "cmd-lore-1", out var res1);
        Assert.True(ok1);
        Assert.True(res1.Mutated);
        Assert.True(game.Progress.Lore[0]);
        Assert.Equal(initialVersion + 1, game.Progress.Version);

        // Second time
        var ok2 = game.TryProcessInteract("b", "lore0", "cmd-lore-2", out var res2);
        Assert.True(ok2);
        Assert.False(res2.Mutated);
        Assert.True(game.Progress.Lore[0]);
        Assert.Equal(initialVersion + 1, game.Progress.Version);
    }

    // 7. Lore không đổi chapter, shard, lỗi hoặc timing
    [Fact]
    public void Lore_DoesNotChangeChapterShardsErrorsOrTimings()
    {
        var game = ReadyNewsGame();
        var shardsBefore = game.Progress.Shards;
        var chapterBefore = game.Progress.Chapter;
        var errorsBefore = game.Progress.WrongAnswerCount;
        var timingCountBefore = game.Progress.ChapterTimings.Count;

        (float X, float Y)[] lorePositions = [(344, 80), (380, 80), (415, 80), (450, 80)];
        for (int i = 0; i < 4; i++)
        {
            MoveMember(game, "a", lorePositions[i].X, lorePositions[i].Y);
            var ok = game.TryProcessInteract("a", $"lore{i}", $"cmd-lore-seq-{i}", out _);
            Assert.True(ok);
            Assert.True(game.Progress.Lore[i]);
        }

        Assert.Equal(chapterBefore, game.Progress.Chapter);
        Assert.Equal(shardsBefore, game.Progress.Shards);
        Assert.Equal(errorsBefore, game.Progress.WrongAnswerCount);
        Assert.Equal(timingCountBefore, game.Progress.ChapterTimings.Count);
    }

    // 8. Đội khác không đổi Clues/Lore
    [Fact]
    public void OtherTeam_DoesNotAffectCluesOrLore()
    {
        var teamA = ReadyNewsGame("teamA");
        var teamB = ReadyNewsGame("teamB");

        teamA.TryProcessInteract("a", "clue0", "cmd-a", out _);
        MoveMember(teamA, "a", 344, 80);
        teamA.TryProcessInteract("a", "lore0", "cmd-lore-a", out _);

        Assert.True(teamA.Progress.Clues[0]);
        Assert.True(teamA.Progress.Lore[0]);

        Assert.False(teamB.Progress.Clues[0]);
        Assert.False(teamB.Progress.Lore[0]);
    }

    // 9. Snapshot clone mảng, client không sửa được state server
    [Fact]
    public void Snapshot_ClonesArrays_ClientCannotMutateServerState()
    {
        var game = ReadyNewsGame();
        var snapshot = game.GetSnapshot();
        var progress = snapshot.Progress;
        Assert.NotNull(progress);
        Assert.NotNull(progress.Lore);

        // Client attempts to mutate arrays directly
        progress.Clues[0] = true;
        progress.Lore[0] = true;

        Assert.False(game.Progress.Clues[0]);
        Assert.False(game.Progress.Lore[0]);
    }

    // 10. Hội thoại local của A không thay panel của B
    [Fact]
    public void LocalDialogueOfA_DoesNotChangePanelOfB()
    {
        var sessionA = new MultiplayerSession("a", "An", "bao");
        var sessionB = new MultiplayerSession("b", "Bình", "bao");

        sessionA.ShowDialogue([new Line("Bảo", "Đối thoại riêng của An.")]);

        Assert.Equal(Panel.Dialogue, sessionA.Panel);
        Assert.Equal(Panel.None, sessionB.Panel);
    }

    // 11. Replay same commandId for interaction returns cached result
    [Fact]
    public void ReplaySameCommandId_ReturnsCachedResultWithoutMutating()
    {
        var game = ReadyNewsGame();
        var ok1 = game.TryProcessInteract("a", "clue0", "cmd-idemp", out var res1);
        var versionAfterFirst = game.Progress.Version;

        var ok2 = game.TryProcessInteract("a", "clue0", "cmd-idemp", out var res2);

        Assert.True(ok1);
        Assert.True(ok2);
        Assert.True(res1.Mutated);
        Assert.False(res2.Mutated);
        Assert.Equal(versionAfterFirst, game.Progress.Version);
    }

    private static void MoveMember(TeamGameInstance game, string playerId, float x, float y)
    {
        var m = game.GetMember(playerId);
        if (m != null)
        {
            m.X = x;
            m.Y = y;
        }
    }

    private static TeamGameInstance ReadyNewsGame(string teamId = "teamRed")
    {
        var game = new TeamGameInstance("match1", "room1", teamId, "Đội Đỏ", "#E53935", StartedAt);
        // Position players within range of News objects (e.g. clue0: 180, 520; clue1: 339, 509; clue2: 286, 552; bao: 259, 537)
        game.AddMember("a", "An", "bao", 180, 520);
        game.AddMember("b", "Bình", "bao", 339, 509);
        game.AddMember("c", "Chi", "bao", 286, 552);

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

    private static TeamGameInstance ReadyGameAtChapter(Chapter chapter)
    {
        var game = new TeamGameInstance("match1", "room1", "teamRed", "Đội Đỏ", "#E53935", StartedAt);
        game.AddMember("a", "An", "bao", 180, 520);
        game.Progress.Chapter = chapter;
        return game;
    }
}

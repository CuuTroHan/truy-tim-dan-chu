using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class CollectionTests
{
    [Fact]
    public void Lamp_Prerequisite_CannotCollectWithoutSpeaking()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.Progress.Chapter = Chapter.Lights;

        // lamp0 tại (125, 260). Player A đứng tại (128, 260)
        teamGame.AddMember("pA", "Player A", "avatar_01", 128f, 260f);

        // Chưa nói chuyện với Phương (Spoken[0] == false)
        Assert.False(teamGame.Progress.Spoken[0]);

        var success = teamGame.TryProcessInteract("pA", "lamp0", "cmd_lamp0", out var res);

        Assert.False(success);
        Assert.Equal("PREREQUISITE_NOT_MET", res.ErrorCode);
        Assert.False(teamGame.Progress.Lamps[0]);
    }

    [Fact]
    public void Lamp_Collect_UpdatesDoneAndIncrementsVersion()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.Progress.Chapter = Chapter.Lights;
        teamGame.Progress.Spoken[0] = true; // Đã nói chuyện với Phương

        teamGame.AddMember("pA", "Player A", "avatar_01", 128f, 260f);

        var success = teamGame.TryProcessInteract("pA", "lamp0", "cmd_lamp0", out var res);

        Assert.True(success);
        Assert.True(res.Mutated);
        Assert.True(teamGame.Progress.Lamps[0]);
        Assert.Equal(2, teamGame.Progress.Version);
    }

    [Fact]
    public async Task Lamp_ConcurrentCollection_Barrier_OnlyMutatesOnce()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.Progress.Chapter = Chapter.Lights;
        teamGame.Progress.Spoken[0] = true;

        teamGame.AddMember("pA", "Player A", "avatar_01", 128f, 260f);
        teamGame.AddMember("pB", "Player B", "avatar_02", 126f, 260f);

        var barrier = new Barrier(2);
        InteractResponse? resA = null;
        InteractResponse? resB = null;

        var tA = Task.Run(() =>
        {
            barrier.SignalAndWait();
            teamGame.TryProcessInteract("pA", "lamp0", "cmd_a", out var r);
            resA = r;
        });

        var tB = Task.Run(() =>
        {
            barrier.SignalAndWait();
            teamGame.TryProcessInteract("pB", "lamp0", "cmd_b", out var r);
            resB = r;
        });

        await Task.WhenAll(tA, tB);

        Assert.True(teamGame.Progress.Lamps[0]);
        // Chính xác 1 người gây mutation
        int mutationCount = (resA!.Mutated ? 1 : 0) + (resB!.Mutated ? 1 : 0);
        Assert.Equal(1, mutationCount);
        // Version chỉ tăng đúng 1 lần (từ 1 lên 2)
        Assert.Equal(2, teamGame.Progress.Version);
    }

    [Fact]
    public void Lamp_CollectTwoDifferentLamps_BothFlagsRecorded()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.Progress.Chapter = Chapter.Lights;
        teamGame.Progress.Spoken[0] = true; // Phương
        teamGame.Progress.Spoken[1] = true; // Dũng

        // lamp0 tại (125, 260), lamp1 tại (267, 260)
        teamGame.AddMember("pA", "Player A", "avatar_01", 128f, 260f);
        teamGame.AddMember("pB", "Player B", "avatar_02", 265f, 260f);

        teamGame.TryProcessInteract("pA", "lamp0", "cmd_lamp0", out var resA);
        teamGame.TryProcessInteract("pB", "lamp1", "cmd_lamp1", out var resB);

        Assert.True(resA.Mutated);
        Assert.True(resB.Mutated);
        Assert.True(teamGame.Progress.Lamps[0]);
        Assert.True(teamGame.Progress.Lamps[1]);
        Assert.Equal(3, teamGame.Progress.Version);
    }

    [Fact]
    public void Lamp_ReCollect_IsIdempotent()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.Progress.Chapter = Chapter.Lights;
        teamGame.Progress.Spoken[0] = true;

        teamGame.AddMember("pA", "Player A", "avatar_01", 128f, 260f);
        teamGame.AddMember("pB", "Player B", "avatar_02", 126f, 260f);

        // A đặt đèn
        teamGame.TryProcessInteract("pA", "lamp0", "cmd_lamp0", out _);
        Assert.True(teamGame.Progress.Lamps[0]);
        Assert.Equal(2, teamGame.Progress.Version);

        // B tương tác lại cùng đèn
        var success = teamGame.TryProcessInteract("pB", "lamp0", "cmd_b_repeat", out var resB);
        Assert.True(success);
        Assert.False(resB.Mutated);
        Assert.Equal(2, teamGame.Progress.Version);
    }

    [Fact]
    public void TeamIsolation_LampsNotSharedAcrossTeams()
    {
        var redTeam = new TeamGameInstance("match1", "room1", "team_red", "Đội Đỏ", "#E53935");
        var blueTeam = new TeamGameInstance("match1", "room1", "team_blue", "Đội Xanh", "#1E88E5");

        redTeam.Progress.Chapter = Chapter.Lights;
        blueTeam.Progress.Chapter = Chapter.Lights;

        redTeam.Progress.Spoken[0] = true;
        blueTeam.Progress.Spoken[0] = true;

        redTeam.AddMember("pA", "Player A", "avatar_01", 128f, 260f);
        blueTeam.AddMember("pC", "Player C", "avatar_03", 500f, 366f);

        // Đội Đỏ đặt lamp0
        redTeam.TryProcessInteract("pA", "lamp0", "cmd_red", out var resRed);
        Assert.True(resRed.Mutated);
        Assert.True(redTeam.Progress.Lamps[0]);

        // Đội Xanh vẫn chưa đặt đèn nào
        Assert.False(blueTeam.Progress.Lamps[0]);
        Assert.Equal(1, blueTeam.Progress.Version);
    }

    [Fact]
    public void AllFourLampsCollected_AllFlagsSet()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.Progress.Chapter = Chapter.Lights;

        for (int i = 0; i < 4; i++)
        {
            teamGame.Progress.Spoken[i] = true;
        }

        // Tọa độ 4 đèn: (125, 260), (267, 260), (125, 388), (267, 388)
        teamGame.AddMember("pA", "Player A", "avatar_01", 125f, 260f);

        for (int i = 0; i < 4; i++)
        {
            var lampCoord = TownCollision.Places[$"lamp{i}"];
            teamGame.UpdateMemberPosition("pA", lampCoord.X, lampCoord.Y, 0, 0);
            teamGame.TryProcessInteract("pA", $"lamp{i}", $"cmd_{i}", out var res);
            Assert.True(res.Mutated);
        }

        Assert.All(teamGame.Progress.Lamps, Assert.True);
        Assert.Equal(5, teamGame.Progress.Version);
    }
}

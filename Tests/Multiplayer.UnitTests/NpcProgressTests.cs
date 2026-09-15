using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class NpcProgressTests
{
    [Fact]
    public void Interact_OutOfRange_ReturnsError()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        // Player A đứng ở (500, 366), Phương ở (170, 280) -> khoảng cách > 300px
        teamGame.AddMember("pA", "Player A", "avatar_01", 500f, 366f);

        var success = teamGame.TryProcessInteract("pA", "phuong", "cmd_1", out var response);

        Assert.False(success);
        Assert.Equal(InteractErrorCodes.OutOfRange, response.ErrorCode);
        Assert.False(response.Mutated);
    }

    [Fact]
    public void Interact_InvalidObject_ReturnsError()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.AddMember("pA", "Player A", "avatar_01", 500f, 366f);

        var success = teamGame.TryProcessInteract("pA", "unknown_npc_99", "cmd_1", out var response);

        Assert.False(success);
        Assert.Equal(InteractErrorCodes.ObjectNotFound, response.ErrorCode);
    }

    [Fact]
    public void Opening_MeetTrong_TransitionsToLights()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        // Trọng ở (526, 300). Player A đứng ở (520, 305) -> khoảng cách ~7.8px <= 31px
        teamGame.AddMember("pA", "Player A", "avatar_01", 520f, 305f);

        Assert.Equal(Chapter.Opening, teamGame.Progress.Chapter);
        Assert.Equal(1, teamGame.Progress.Version);

        var success = teamGame.TryProcessInteract("pA", "trong", "cmd_start", out var response);

        Assert.True(success);
        Assert.True(response.Mutated);
        Assert.Equal(Chapter.Lights, teamGame.Progress.Chapter);
        Assert.Equal(2, teamGame.Progress.Version);
    }

    [Fact]
    public void Opening_ReplaySameCommand_IsIdempotent()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.AddMember("pA", "Player A", "avatar_01", 520f, 305f);

        // Lần 1: Thành công và mutation
        teamGame.TryProcessInteract("pA", "trong", "cmd_same", out var res1);
        Assert.True(res1.Mutated);
        Assert.Equal(2, teamGame.Progress.Version);

        // Lần 2: Replay cùng CommandId
        var success2 = teamGame.TryProcessInteract("pA", "trong", "cmd_same", out var res2);
        Assert.True(success2);
        Assert.False(res2.Mutated); // Không mutation lần 2
        Assert.Equal(2, teamGame.Progress.Version); // Version không bị tăng thừa
    }

    [Fact]
    public void Lights_MeetNpc_SetsSpokenFlag_Atomically()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        // Chuyển sang Chapter.Lights
        teamGame.Progress.Chapter = Chapter.Lights;

        // Phương ở (170, 280). Player A đứng tại (175, 280) -> khoảng cách 5px <= 31px
        teamGame.AddMember("pA", "Player A", "avatar_01", 175f, 280f);

        Assert.False(teamGame.Progress.Spoken[0]);

        var success = teamGame.TryProcessInteract("pA", "phuong", "cmd_phuong", out var response);

        Assert.True(success);
        Assert.True(response.Mutated);
        Assert.True(teamGame.Progress.Spoken[0]);
        Assert.Equal(2, teamGame.Progress.Version);
    }

    [Fact]
    public void Lights_TwoPlayersMeetSameNpc_OnlyMutatesOnce()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.Progress.Chapter = Chapter.Lights;

        // A và B cùng đứng gần Phương (170, 280)
        teamGame.AddMember("pA", "Player A", "avatar_01", 172f, 280f);
        teamGame.AddMember("pB", "Player B", "avatar_02", 174f, 280f);

        // A tương tác trước
        var successA = teamGame.TryProcessInteract("pA", "phuong", "cmd_a", out var resA);
        Assert.True(successA);
        Assert.True(resA.Mutated);
        Assert.Equal(2, teamGame.Progress.Version);

        // B tương tác sau với cùng NPC
        var successB = teamGame.TryProcessInteract("pB", "phuong", "cmd_b", out var resB);
        Assert.True(successB);
        Assert.False(resB.Mutated); // Đã có cờ, không mutation lặp
        Assert.Equal(2, teamGame.Progress.Version); // Version không đổi
        Assert.True(teamGame.Progress.Spoken[0]);
    }

    [Fact]
    public void Lights_TwoPlayersMeetDifferentNpcs_KeepsBothFlags()
    {
        var teamGame = new TeamGameInstance("match1", "room1", "team1", "Đội Đỏ", "#E53935");
        teamGame.Progress.Chapter = Chapter.Lights;

        // Phương ở (170, 280). Dũng ở (205, 338).
        // Player A gần Phương, Player B gần Dũng
        teamGame.AddMember("pA", "Player A", "avatar_01", 172f, 280f);
        teamGame.AddMember("pB", "Player B", "avatar_02", 208f, 338f);

        // A gặp Phương
        teamGame.TryProcessInteract("pA", "phuong", "cmd_a", out var resA);
        Assert.True(resA.Mutated);

        // B gặp Dũng
        teamGame.TryProcessInteract("pB", "dung", "cmd_b", out var resB);
        Assert.True(resB.Mutated);

        // Cả 2 cờ đều được lưu cho toàn đội
        Assert.True(teamGame.Progress.Spoken[0]); // Phương
        Assert.True(teamGame.Progress.Spoken[1]); // Dũng
        Assert.Equal(3, teamGame.Progress.Version);
    }

    [Fact]
    public void Client_DiscardsOlderSnapshot()
    {
        var session = new MultiplayerSession("pA", "Player A", "avatar_01");
        // State version 3
        var snapV3 = new TeamGameStateSnapshot(
            "match1", "team1", "Đội Đỏ", "#E53935",
            [new TeamMemberState("pA", "Player A", "avatar_01", "#E53935", 500f, 366f, 0, 0, true)],
            new TeamProgressSnapshot(Chapter.Lights, [true, true, false, false], new bool[4], new bool[4], 3, 0)
        );
        session.ApplySnapshot(snapV3);

        Assert.True(session.Spoken[0]);
        Assert.True(session.Spoken[1]);
        Assert.Equal(3, session.Progress.Version);

        // Giả lập snapshot version 2 (gói tin cũ đến trễ do mạng)
        var snapV2 = new TeamGameStateSnapshot(
            "match1", "team1", "Đội Đỏ", "#E53935",
            [new TeamMemberState("pA", "Player A", "avatar_01", "#E53935", 500f, 366f, 0, 0, true)],
            new TeamProgressSnapshot(Chapter.Lights, [true, false, false, false], new bool[4], new bool[4], 2, 0)
        );
        session.ApplySnapshot(snapV2);

        // Không bị kéo lùi state: cờ Spoken[1] vẫn là true, version vẫn là 3
        Assert.True(session.Spoken[1]);
        Assert.Equal(3, session.Progress.Version);
    }

    [Fact]
    public void TeamIsolation_ProgressNotSharedAcrossTeams()
    {
        var redTeam = new TeamGameInstance("match1", "room1", "team_red", "Đội Đỏ", "#E53935");
        var blueTeam = new TeamGameInstance("match1", "room1", "team_blue", "Đội Xanh", "#1E88E5");

        redTeam.Progress.Chapter = Chapter.Lights;
        blueTeam.Progress.Chapter = Chapter.Lights;

        redTeam.AddMember("pA", "Player A", "avatar_01", 172f, 280f);
        blueTeam.AddMember("pC", "Player C", "avatar_03", 500f, 366f);

        // Đội Đỏ gặp Phương
        redTeam.TryProcessInteract("pA", "phuong", "cmd_red", out var resRed);
        Assert.True(resRed.Mutated);
        Assert.True(redTeam.Progress.Spoken[0]);

        // Đội Xanh không hề bị ảnh hưởng
        Assert.False(blueTeam.Progress.Spoken[0]);
        Assert.Equal(1, blueTeam.Progress.Version);
    }
}

